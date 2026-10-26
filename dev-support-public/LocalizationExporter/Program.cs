using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

internal static class Program
{
    private const string ExporterVersion = "1.2.0";
    private const string ManifestName = "BookStitch.LocalizationExport.json";
    private static readonly UTF8Encoding Utf8NoBom = new(false, true);
    private static readonly Regex HashPattern = new(@"(?m)^SourceHash:\s*([a-f0-9]{64})\s*$", RegexOptions.Compiled);
    private static readonly Regex FormatPattern = new(@"\{(?<id>\d+)(?:,[^{}]*)?(?::[^{}]*)?\}", RegexOptions.Compiled);
    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private sealed record Options(string Mode, string MasterPath, string ResourcesPath, string? AcceptFile, string StateDirectory, bool CopyReportsToResources);
    private sealed record OutputFile(string Locale, string Name, byte[] Bytes);
    private sealed record Diagnostic(string Severity, string Code, string Key, string Locale, string Detail)
    {
        public string Category => Severity == "warning" ? "linguistic-quality" : "technical";
    }
    private sealed class Audit
    {
        public List<Diagnostic> Messages { get; } = [];
        public int Errors => Messages.Count(x => x.Severity == "error");
        public int Warnings => Messages.Count(x => x.Severity == "warning");
        public void Error(string code, string key, string locale, string detail) => Messages.Add(new("error", code, key, locale, detail));
        public void Warn(string code, string key, string locale, string detail) => Messages.Add(new("warning", code, key, locale, detail));
    }

    private static int Main(string[] args)
    {
        try
        {
            var options = ParseOptions(args);
            if (!File.Exists(options.MasterPath)) throw new InvalidOperationException("Language Master fehlt: " + options.MasterPath);
            JsonObject root = JsonNode.Parse(File.ReadAllText(options.MasterPath, Utf8NoBom))?.AsObject()
                ?? throw new InvalidOperationException("Master JSON muss ein Objekt sein.");
            return options.Mode switch
            {
                "initializebaselines" => InitializeBaselines(root, options),
                "accepttranslations" => AcceptTranslations(root, options),
                "development" or "release" or "verify" => ExportOrVerify(root, options),
                _ => throw new InvalidOperationException("Ungültiger Modus: " + options.Mode)
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("FEHLER: " + ex.Message);
            try
            {
                var options = ParseOptions(args);
                var audit = new Audit();
                audit.Error("EXPORT_FAILURE", "", "", ex.Message);
                WriteReport(options, audit, "failed", []);
            }
            catch (Exception reportError) { Console.Error.WriteLine("Report unavailable: " + reportError.Message); }
            return 1;
        }
    }

    private static Options ParseOptions(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < args.Length; i += 2)
        {
            if (i + 1 >= args.Length || !args[i].StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException("Erwartet: --mode MODE --master PATH --resources PATH [--accept-file PATH] [--state-directory PATH]");
            string key = args[i][2..];
            if (!values.TryAdd(key, args[i + 1])) throw new ArgumentException("Argument doppelt: " + key);
        }
        foreach (string k in values.Keys)
            if (k is not ("mode" or "master" or "resources" or "accept-file" or "state-directory")) throw new ArgumentException("Unbekanntes Argument: " + k);
        string mode = values.GetValueOrDefault("mode", "verify").ToLowerInvariant();
        string master = Path.GetFullPath(values.GetValueOrDefault("master") ?? throw new ArgumentException("--master fehlt"));
        string resources = Path.GetFullPath(values.GetValueOrDefault("resources") ?? throw new ArgumentException("--resources fehlt"));
        string stateDirectory = Path.GetFullPath(values.GetValueOrDefault("state-directory")
            ?? Path.Combine(AppContext.BaseDirectory, "..", "..", ".."));
        return new Options(mode, master, resources, values.GetValueOrDefault("accept-file"), stateDirectory, !values.ContainsKey("state-directory"));
    }

    private static JsonObject Object(JsonNode? node, string where) =>
        node as JsonObject ?? throw new InvalidOperationException("JSON-Objekt fehlt: " + where);
    private static JsonArray Array(JsonNode? node, string where) =>
        node as JsonArray ?? throw new InvalidOperationException("JSON-Array fehlt: " + where);
    private static string Str(JsonNode? node, string where) =>
        node?.GetValue<string>() ?? throw new InvalidOperationException("JSON-Text fehlt: " + where);
    private static string? OptionalStr(JsonNode? node) => node is null ? null : node.GetValue<string>();
    private static string Sha(byte[] bytes) => Convert.ToHexStringLower(SHA256.HashData(bytes));
    private static string Sha(string text) => Sha(Utf8NoBom.GetBytes(text));
    // Python json.dumps(ensure_ascii=False, separators=(',', ':'), allow_nan=False).
    // Even UnsafeRelaxedJsonEscaping escapes Unicode blocklisted by System.Text.Json
    // (including NBSP). Fingerprints require the exact canonical bytes instead.
    private static string ReviewFingerprint(string de, string comment, string target)
    {
        var json = new StringBuilder("[");
        foreach (string value in new[] { de, comment, target })
        {
            if (json.Length > 1) json.Append(',');
            json.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': json.Append("\\\""); break;
                    case '\\': json.Append("\\\\"); break;
                    case '\b': json.Append("\\b"); break;
                    case '\f': json.Append("\\f"); break;
                    case '\n': json.Append("\\n"); break;
                    case '\r': json.Append("\\r"); break;
                    case '\t': json.Append("\\t"); break;
                    default:
                        if (c < 0x20) json.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else json.Append(c);
                        break;
                }
            }
            json.Append('"');
        }
        return Sha(json.Append(']').ToString());
    }
    private static string GetRule(string comment)
    {
        var match = Regex.Match(comment, @"(?m)^TranslationRule:\s*(\S+)\s*$", RegexOptions.CultureInvariant);
        return match.Success ? match.Groups[1].Value : "";
    }
    private static bool HasMarker(string comment, string marker) =>
        Regex.IsMatch(comment, @"(?m)^" + Regex.Escape(marker) + @"\s*(?:\r?$)");
    private static string? SourceHash(string comment)
    {
        Match match = HashPattern.Match(comment);
        return match.Success ? match.Groups[1].Value : null;
    }
    private static string SetSourceHash(string comment, string hash) =>
        HashPattern.IsMatch(comment) ? HashPattern.Replace(comment, "SourceHash: " + hash, 1)
        : (string.IsNullOrEmpty(comment) ? "" : comment.TrimEnd('\r', '\n') + "\n") + "SourceHash: " + hash;

    private static IReadOnlyDictionary<string, int> Placeholders(string text)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        // Parse the complete format first: a regex alone misses malformed braces.
        CompositeFormat.Parse(text);
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] != '{') continue;
            if (i + 1 < text.Length && text[i + 1] == '{') { i++; continue; }
            var match = FormatPattern.Match(text, i);
            if (!match.Success || match.Index != i) throw new FormatException("Invalid format item.");
            // Preserve alignment and format specifiers as well as index/multiplicity.
            string token = match.Value;
            result[token] = result.GetValueOrDefault(token) + 1;
            i = match.Index + match.Length - 1;
        }
        return result;
    }
    private static bool SamePlaceholders(string de, string target)
    {
        try
        {
            var first = Placeholders(de); var second = Placeholders(target);
            return first.Count == second.Count && first.All(kv => second.GetValueOrDefault(kv.Key, -1) == kv.Value);
        }
        catch (FormatException) { return false; }
    }
    private static bool ValidXmlText(string input)
    {
        try { XmlConvert.VerifyXmlChars(input); return true; }
        catch (XmlException) { return false; }
    }

    private static (JsonObject entries, JsonArray locales, JsonObject layout) Core(JsonObject root)
    {
        if (Str(root["sourceLocale"], "sourceLocale") != "de")
            throw new InvalidOperationException("Nur German sourceLocale=de unterstützt.");
        return (Object(root["entries"], "entries"), Array(root["locales"], "locales"), Object(root["resxLayout"], "resxLayout"));
    }

    // Explicit one-time migration. Establishes change-tracking baselines, NOT language review approvals.
    private static int InitializeBaselines(JsonObject root, Options options)
    {
        var (entries, locales, _) = Core(root);
        if (entries.Any(kv => (kv.Value as JsonObject)?.ContainsKey("translationBase") == true))
            throw new InvalidOperationException("translationBase existiert bereits. Initialisierung ist absichtlich nur einmal möglich.");
        var localeIds = locales.Select(x => Str(Object(x, "locale")["locale"], "locale")).Where(x => x != "de").ToArray();
        int initialized = 0;
        foreach (var (key, value) in entries)
        {
            var entry = Object(value, "entries." + key);
            var source = Object(entry["source"], key + ".source");
            string de = Str(source["value"], key + ".source.value");
            string comment = Str(source["comment"], key + ".source.comment");
            string expectedLegacy = Sha(de);
            string rule = GetRule(comment);
            var translations = Object(entry["translations"], key + ".translations");
            var baseline = new JsonObject();
            foreach (var locale in localeIds)
            {
                var tr = Object(translations[locale], key + "." + locale);
                string text = Str(tr["value"], key + "." + locale + ".value");
                string trComment = OptionalStr(tr["comment"]) ?? "";
                if (SourceHash(trComment) != expectedLegacy)
                    throw new InvalidOperationException($"Migration gestoppt: alter SourceHash fehlt/weicht ab: {key}/{locale}");
                baseline[locale] = CreateBase(de, comment, text, rule == "GermanEnglish" ? Str(Object(translations["en"], key + ".en")["value"], key + ".en.value") : null,
                    "initial-snapshot-not-linguistically-approved");
                initialized++;
            }
            entry["translationBase"] = baseline;
        }
        SaveMaster(root, options, "before-baseline-initialization");
        Console.WriteLine($"OK: {initialized} Übersetzungs-Baselines angelegt. Keine Sprachfreigaben erzeugt.");
        return 0;
    }

    private static JsonObject CreateBase(string de, string comment, string text, string? germanEnglishReference, string provenance)
    {
        var result = new JsonObject
        {
            ["sourceValueSha256"] = Sha(de),
            ["sourceCommentSha256"] = Sha(comment),
            ["targetValueSha256"] = Sha(text),
            ["basis"] = provenance
        };
        if (germanEnglishReference is not null) result["englishReferenceSha256"] = Sha(germanEnglishReference);
        return result;
    }

    // Accepts an explicit JSON array of {"key":"...","locale":"...","note":"reviewed"}.
    // Never edits translated values; records that a human/LLM translation revision was performed.
    private static int AcceptTranslations(JsonObject root, Options options)
    {
        if (options.AcceptFile is null) throw new InvalidOperationException("--accept-file PATH mit expliziter Schlüsselliste fehlt.");
        var decisions = JsonNode.Parse(File.ReadAllText(options.AcceptFile, Utf8NoBom)) as JsonArray
            ?? throw new InvalidOperationException("Accept-Datei muss ein JSON-Array sein.");
        if (decisions.Count == 0) throw new InvalidOperationException("Leere Accept-Datei.");
        var (entries, _, _) = Core(root);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var decisionNode in decisions)
        {
            var decision = Object(decisionNode, "accept item");
            string key = Str(decision["key"], "accept.key");
            string locale = Str(decision["locale"], "accept.locale");
            string note = Str(decision["note"], "accept.note");
            if (locale == "de" || string.IsNullOrWhiteSpace(note) || note.Length < 5)
                throw new InvalidOperationException("Ungültige Accept-Angabe (de oder Notiz zu kurz): " + key + "/" + locale);
            if (!seen.Add(key + "\u0001" + locale)) throw new InvalidOperationException("Doppelte Accept-Angabe: " + key + "/" + locale);
            var entry = Object(entries[key], "entries." + key);
            var source = Object(entry["source"], key + ".source");
            string de = Str(source["value"], key + ".de");
            string comment = Str(source["comment"], key + ".comment");
            var translations = Object(entry["translations"], key + ".translations");
            var tr = Object(translations[locale], key + "." + locale);
            string target = Str(tr["value"], key + "." + locale + ".value");
            string trComment = OptionalStr(tr["comment"]) ?? "";
            bool locked = HasMarker(comment, "Lock: global") || HasMarker(trComment, "Lock: language");
            bool overrideLock = decision["overrideLock"]?.GetValue<bool>() == true;
            if (locked && !overrideLock)
                throw new InvalidOperationException("Locked text requires explicit overrideLock: true: " + key + "/" + locale);
            if (overrideLock && (!locked || note.Length < 20))
                throw new InvalidOperationException("Lock override requires locked text and a specific reason (at least 20 characters).");
            if (entry["uiFit"] is JsonObject ui && ui[locale] is JsonObject uf && OptionalStr(uf["status"]) == "pending_visual_verification")
                throw new InvalidOperationException("UI-Kurzfassung wartet auf Screenshot-Nachtest: " + key + "/" + locale);
            if (!SamePlaceholders(de, target)) throw new InvalidOperationException("Platzhalter weichen ab: " + key + "/" + locale);
            string rule = GetRule(comment);
            if (rule == "KeepOriginal" && target != de) throw new InvalidOperationException("KeepOriginal verletzt: " + key + "/" + locale);
            string? en = rule == "GermanEnglish" ? Str(Object(translations["en"], key + ".en")["value"], key + ".en.value") : null;
            if (rule == "GermanEnglish" && target != en) throw new InvalidOperationException("GermanEnglish verletzt: " + key + "/" + locale);
            if (entry["translationBase"] is null) entry["translationBase"] = new JsonObject();
            var bases = Object(entry["translationBase"], key + ".translationBase");
            if (overrideLock)
            {
                if (entry["lockOverrides"] is null) entry["lockOverrides"] = new JsonArray();
                Array(entry["lockOverrides"], key + ".lockOverrides").Add(new JsonObject
                {
                    ["locale"] = locale, ["note"] = note, ["acceptedAtUtc"] = DateTime.UtcNow.ToString("O"),
                    ["previousBase"] = bases[locale]?.DeepClone(),
                    ["acceptedBase"] = CreateBase(de, comment, target, en, "explicit-lock-override")
                });
            }
            bases[locale] = CreateBase(de, comment, target, en, "explicit-translation-review: " + note);
            tr["comment"] = SetSourceHash(trComment, Sha(de));
        }
        SaveMaster(root, options, "before-translation-acceptance");
        Console.WriteLine($"OK: {seen.Count} ausdrücklich geprüfte Übersetzungs-Baselines übernommen; keine Reviews erfunden.");
        return 0;
    }

    private static void SaveMaster(JsonObject root, Options options, string backupTag)
    {
        string backupDir = Path.Combine(options.StateDirectory, "backups");
        Directory.CreateDirectory(backupDir);
        string backup = Path.Combine(backupDir, $"BookStitch.LanguageMaster.{backupTag}.{DateTime.UtcNow:yyyyMMddTHHmmssfffZ}.{Guid.NewGuid():N}.json");
        File.Copy(options.MasterPath, backup);
        string temp = options.MasterPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temp, root.ToJsonString(PrettyJson) + "\n", Utf8NoBom);
            using (var json = JsonDocument.Parse(File.ReadAllBytes(temp))) { }
            File.Move(temp, options.MasterPath, true);
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
        Console.WriteLine("Sicherung: " + Path.GetFullPath(backup));
    }

    private static int ExportOrVerify(JsonObject root, Options options)
    {
        var (entries, locales, layout) = Core(root);
        bool verify = options.Mode == "verify";
        var audit = new Audit();
        var output = new List<OutputFile>();
        string? englishFor(string key)
        {
            var e = Object(entries[key], key);
            var trans = Object(e["translations"], key + ".translations");
            return OptionalStr((trans["en"] as JsonObject)?["value"]);
        }
        if (entries.Count == 0) throw new InvalidOperationException("Master enthält keine Ressourcen-Schlüssel.");
        foreach (var node in locales)
        {
            var loc = Object(node, "locales[]");
            string locale = Str(loc["locale"], "locale");
            string fileName = Str(loc["fileName"], locale + ".fileName");
            if (fileName != $"LocalizationResources.{locale}.resx" || fileName.IndexOfAny(new[] { '/', '\\' }) >= 0)
                throw new InvalidOperationException("Ungültiger RESX-Dateiname: " + fileName);
            var rows = new List<(string Key, string Value, string Comment)>();
            var order = GetOrder(locale, entries, layout, audit);
            foreach (string key in order)
            {
                var entry = Object(entries[key], key);
                var source = Object(entry["source"], key + ".source");
                string de = Str(source["value"], key + ".de");
                string deComment = Str(source["comment"], key + ".de.comment");
                if (string.IsNullOrWhiteSpace(de) || string.IsNullOrWhiteSpace(deComment))
                    audit.Error("MISSING_SOURCE", key, "de", "Deutscher Wert oder Entwicklerkommentar fehlt.");
                var trans = Object(entry["translations"], key + ".translations");
                string en = englishFor(key) ?? "";
                if (string.IsNullOrWhiteSpace(en)) audit.Error("MISSING_ENGLISH", key, "en", "Englische Referenz fehlt.");
                string rule = GetRule(deComment);
                if (rule != "" && rule is not ("KeepOriginal" or "GermanEnglish"))
                    audit.Error("UNKNOWN_RULE", key, "de", rule);
                if (rule == "GermanEnglish" && locale != "de" && string.IsNullOrEmpty(en))
                    audit.Error("MISSING_GERMAN_ENGLISH", key, locale, "Englische Referenz für feste Regel fehlt.");
                string value, comment;
                if (locale == "de")
                {
                    value = de;
                    comment = deComment;
                }
                else
                {
                    var target = trans[locale] as JsonObject;
                    if (target is null || OptionalStr(target["value"]) is not string targetValue || string.IsNullOrWhiteSpace(targetValue))
                    {
                        if (HasMarker(deComment, "Lock: global"))
                            audit.Warn("LOCK_BASELINE_CONFLICT", key, locale, "Locked entry is missing its target value.");
                        if (locale == "en") audit.Error("MISSING_ENGLISH", key, locale, "Englische Referenz fehlt.");
                        audit.Warn("DEV_FALLBACK", key, locale, "Missing target uses temporary German/English fallback; not a reviewed translation.");
                        value = rule == "GermanEnglish" && !string.IsNullOrEmpty(en) ? en : de;
                        comment = "DevelopmentFallback: de; not a reviewed translation";
                    }
                    else
                    {
                        value = targetValue;
                        comment = OptionalStr(target["comment"]) ?? "";
                        if (string.IsNullOrWhiteSpace(comment) && locale == "en")
                            comment = "SourceHash: " + Sha(de) + "\nDevelopmentReference: English source maintained in LanguageMaster";
                        if (SourceHash(comment) != Sha(de))
                            ReportStale(audit, "SOURCE_HASH_STALE", key, locale, "Deutscher Originaltext seit letztem SourceHash geändert.");
                        var baseline = (entry["translationBase"] as JsonObject)?[locale] as JsonObject;
                        if (baseline is null)
                        {
                            ReportStale(audit, "BASELINE_MISSING", key, locale, "Baseline fehlt; zuerst einmalig initialisieren oder Zieltext ausdrücklich prüfen.");
                        }
                        else
                        {
                            bool changedSource = OptionalStr(baseline["sourceValueSha256"]) != Sha(de)
                                || OptionalStr(baseline["sourceCommentSha256"]) != Sha(deComment);
                            bool changedTarget = OptionalStr(baseline["targetValueSha256"]) != Sha(value);
                            bool changedEnglish = rule == "GermanEnglish" && OptionalStr(baseline["englishReferenceSha256"]) != Sha(en);
                            if (changedSource || changedTarget || changedEnglish)
                                ReportStale(audit, "BASELINE_STALE", key, locale,
                                    $"Quelltext/Kommentar geändert={changedSource}; Zieltext geändert={changedTarget}; englische Referenz geändert={changedEnglish}.");
                        }
                        bool locked = HasMarker(comment, "Lock: language") || HasMarker(deComment, "Lock: global");
                        if (locked && (baseline is null ||
                            OptionalStr(baseline["sourceValueSha256"]) != Sha(de) ||
                            OptionalStr(baseline["sourceCommentSha256"]) != Sha(deComment) ||
                            OptionalStr(baseline["targetValueSha256"]) != Sha(value) ||
                            (rule == "GermanEnglish" && OptionalStr(baseline["englishReferenceSha256"]) != Sha(en))))
                            audit.Warn("LOCK_BASELINE_CONFLICT", key, locale,
                                "Locked source context/target changed or baseline missing. Explicit per-entry lock override required.");
                        if (HasMarker(comment, "Lock: language") && (SourceHash(comment) != Sha(de)))
                            audit.Warn("LOCK_CONFLICT", key, locale, "Sprachspezifische Sperre kollidiert mit geändertem Ursprung.");
                        if (HasMarker(deComment, "Lock: global") && (SourceHash(comment) != Sha(de)))
                            audit.Warn("LOCK_CONFLICT", key, locale, "Globale Sperre kollidiert mit geändertem Ursprung.");
                    }
                    if (rule == "KeepOriginal" && value != de)
                        audit.Warn("KEEP_ORIGINAL_VIOLATION", key, locale, "Zielwert muss exakt dem deutschen Wert entsprechen.");
                    if (rule == "GermanEnglish" && value != en)
                        audit.Warn("GERMAN_ENGLISH_VIOLATION", key, locale, "Zielwert muss exakt englischem Referenzwert entsprechen.");
                    if (!SamePlaceholders(de, value))
                        audit.Error("PLACEHOLDER_MISMATCH", key, locale, "Unterschiedliche .NET-Formatplatzhalter.");
                    if (entry["uiFit"] is JsonObject fit && fit[locale] is JsonObject uf)
                    {
                        string actualFingerprint = ReviewFingerprint(de, deComment, value);
                        string? uiStatus = OptionalStr(uf["status"]);
                        if (uiStatus == "pending_visual_verification")
                        {
                            if (OptionalStr(uf["candidateFingerprint"]) != actualFingerprint)
                                audit.Warn("UI_FIT_GUARD_VIOLATION", key, locale, "Geschützte UI-Kurzfassung oder Quellkontext ohne Nachtest geändert.");
                            else audit.Warn("UI_FIT_PENDING", key, locale, "Kurzform noch nicht im WPF-Screenshot nachgetestet.");
                        }
                        else if (uiStatus is "verified" or "approved")
                        {
                            if (OptionalStr(uf["approvedFingerprint"]) != actualFingerprint)
                                audit.Warn("UI_FIT_APPROVAL_STALE", key, locale, "Visueller UI-Fit-Nachweis ist nicht mehr aktuell.");
                        }
                    }
                    if (entry["reviews"] is JsonObject reviews && reviews[locale] is JsonObject review)
                    {
                        string? status = OptionalStr(review["status"]);
                        if (OptionalStr(review["fingerprint"]) is string recordedFingerprint &&
                            recordedFingerprint != ReviewFingerprint(de, deComment, value))
                            audit.Warn("REVIEW_STALE", key, locale, "Historische Sprachprüfung passt nicht mehr zu Text und Entwicklerkommentar.");
                        if (status == "needs_correction")
                        {
                            if (review["releaseBlocking"]?.GetValue<bool>() == true)
                                audit.Warn("BLOCKING_REVIEW", key, locale, "Release-blockierender offener Auditbefund.");
                            else audit.Warn("REVIEW_OPEN", key, locale, "Sprachlicher Auditbefund offen / Kontextprüfung nötig.");
                        }
                    }
                }
                try { CompositeFormat.Parse(value); }
                catch (FormatException ex) { audit.Error("INVALID_COMPOSITE_FORMAT", key, locale, ex.Message); }
                if (!ValidXmlText(key) || !ValidXmlText(value) || !ValidXmlText(comment))
                    audit.Error("INVALID_XML", key, locale, "Unzulässige XML-Zeichen.");
                rows.Add((key, value, comment));
            }
            output.Add(new OutputFile(locale, fileName, ResxBytes(rows, layout, locale)));
        }
        if (output.Count != locales.Count || output.Select(f => f.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != output.Count)
            audit.Error("LOCALE_DUPLICATE", "", "", "Doppelte oder fehlende Locale-Dateien.");
        var masterHash = Sha(File.ReadAllBytes(options.MasterPath));
        var inputDigest = ResourceDigest(output);
        PrintSummary(output, audit);
        if (verify) return VerifyExport(output, options, audit, masterHash, inputDigest);
        if (audit.Errors > 0)
        {
            WriteReport(options, audit, "failed", output);
            Console.Error.WriteLine($"Export abgebrochen: {audit.Errors} Fehler. Bestehende RESX/Manifest nicht verändert.");
            return 2;
        }
        Directory.CreateDirectory(options.ResourcesPath);
        string stage = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "work", "BookStitch-Resx-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stage);
        try
        {
            foreach (var f in output) File.WriteAllBytes(Path.Combine(stage, f.Name), f.Bytes);
            foreach (var f in output)
            {
                byte[] staged = File.ReadAllBytes(Path.Combine(stage, f.Name));
                CheckXml(staged, f, entries, audit);
            }
            if (audit.Errors > 0)
            {
                WriteReport(options, audit, "failed", output);
                Console.Error.WriteLine("XML-Rückleseprüfung fehlgeschlagen; Ausgabe nicht verändert.");
                return 2;
            }
            string manifest = BuildManifest(options, output, masterHash, inputDigest, audit);
            File.WriteAllText(Path.Combine(stage, ManifestName), manifest, Utf8NoBom);
            Publish(stage, options.ResourcesPath, output.Select(f => f.Name).Append(ManifestName));
            WriteReport(options, audit, "exported", output);
            Console.WriteLine("OK: " + output.Count + " RESX-Dateien + Manifest veröffentlicht (" + options.Mode + ").");
            return 0;
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }

    private static void ReportStale(Audit audit, string code, string key, string locale, string detail)
    {
        // Source/translation freshness is a quality finding, never an export veto.
        audit.Warn(code, key, locale, detail);
    }

    private static List<string> GetOrder(string locale, JsonObject entries, JsonObject layout, Audit audit)
    {
        var keys = entries.Select(kv => kv.Key).ToList();
        var overrides = layout["originalPerLocaleOrderOverrides"] as JsonObject;
        if (overrides?[locale] is not JsonArray ordered) return keys;
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var node in ordered)
        {
            string key = Str(node, locale + ".order");
            if (!entries.ContainsKey(key)) continue; // Deleted key intentionally absent from master.
            if (!seen.Add(key)) audit.Error("DUPLICATE_KEY_ORDER", key, locale, "Schlüssel mehrfach im Exportprofil.");
            result.Add(key);
        }
        foreach (string key in keys) if (seen.Add(key)) result.Add(key); // New keys appended.
        return result;
    }

    private static string XmlText(string text) => text.Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
    private static string XmlAttribute(string text) => XmlText(text).Replace("\"", "&quot;", StringComparison.Ordinal);

    // Uses each locale's historical whitespace profile; unchanged master data can retain identical bytes.
    private static byte[] ResxBytes(List<(string Key, string Value, string Comment)> rows, JsonObject layout, string locale)
    {
        var profiles = Object(layout["originalWhitespaceProfiles"], "resxLayout.originalWhitespaceProfiles");
        var profile = profiles[locale] as JsonObject;
        string declaration = OptionalStr(profile?["xmlDeclaration"]) ?? Str(layout["xmlDeclaration"], "xmlDeclaration");
        string rootName = Str(layout["rootElement"], "rootElement");
        string dataTag = Str(layout["dataElement"], "dataElement");
        int headerAt = layout["headersInsertedBeforeDataIndex"]?.GetValue<int>() ?? 16;
        if (rootName != "root" || dataTag != "data") throw new InvalidOperationException("Unbekanntes RESX-Layout.");
        var headers = Array(layout["resheaders"], "resxLayout.resheaders");
        var rawPatterns = profile?["patterns"] as JsonArray;
        var rawRuns = profile?["patternRuns"] as JsonArray;
        var orderedPatterns = new List<(string? InnerText, string? ElementTail, string? FirstChildTail, string? SecondChildTail)>();
        if (rawRuns is not null && rawPatterns is not null)
        {
            foreach (var pair in rawRuns)
            {
                var a = Array(pair, "patternRuns[]");
                int id = a[0]!.GetValue<int>(); int patternCount = a[1]!.GetValue<int>();
                var p = Array(rawPatterns[id], "patterns[]");
                var children = Array(p[2], "patterns children");
                for (int i = 0; i < patternCount; ++i)
                    orderedPatterns.Add((OptionalStr(p[0]), OptionalStr(p[1]),
                        OptionalStr(children.Count > 0 ? children[0] : null),
                        OptionalStr(children.Count > 1 ? children[1] : null)));
            }
        }
        var sb = new StringBuilder(512 * 1024);
        sb.Append(declaration).Append("\n<root>").Append(OptionalStr(profile?["rootText"]) ?? "\n  ");
        int nodeIndex = 0;
        int totalNodes = rows.Count + headers.Count;
        (string? InnerText, string? ElementTail, string? FirstChildTail, string? SecondChildTail) Pattern()
        {
            int current = nodeIndex++;
            (string? InnerText, string? ElementTail, string? FirstChildTail, string? SecondChildTail) p =
                current < orderedPatterns.Count ? orderedPatterns[current] :
                ("\n    ", "\n  ", "\n    ", "\n  ");
            // For added/deleted keys, adjust the final sibling tail instead of copying an obsolete final tail.
            if (totalNodes != orderedPatterns.Count)
            {
                string tail = current == totalNodes - 1 ? "\n" : p.ElementTail is null or "\n" ? "\n  " : p.ElementTail;
                return (p.InnerText, tail, p.FirstChildTail, p.SecondChildTail);
            }
            return p;
        }
        void WriteHeader(JsonObject header)
        {
            var p = Pattern();
            sb.Append("<resheader name=\"").Append(XmlAttribute(Str(header["name"], "resheader.name"))).Append("\">")
                .Append(p.InnerText).Append("<value>").Append(XmlText(Str(header["value"], "resheader.value")))
                .Append("</value>").Append(p.FirstChildTail).Append("</resheader>").Append(p.ElementTail);
        }
        int count = 0;
        foreach (var row in rows)
        {
            if (count == headerAt) foreach (var item in headers) WriteHeader(Object(item, "resheaders[]"));
            var p = Pattern();
            sb.Append("<data name=\"").Append(XmlAttribute(row.Key)).Append("\" xml:space=\"preserve\">")
                .Append(p.InnerText).Append("<value>").Append(XmlText(row.Value)).Append("</value>")
                .Append(p.FirstChildTail).Append("<comment>").Append(XmlText(row.Comment)).Append("</comment>")
                .Append(p.SecondChildTail).Append("</data>").Append(p.ElementTail);
            count++;
        }
        if (count <= headerAt) foreach (var item in headers) WriteHeader(Object(item, "resheaders[]"));
        sb.Append("</root>").Append(OptionalStr(profile?["rootTail"]));
        if (profile?["trailingNewline"]?.GetValue<bool>() ?? true) sb.Append('\n');
        return Utf8NoBom.GetBytes(sb.ToString());
    }

    private static void CheckXml(byte[] bytes, OutputFile file, JsonObject entries, Audit audit)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(ms, settings);
            var doc = XDocument.Load(reader, LoadOptions.PreserveWhitespace);
            if (doc.Root?.Name.LocalName != "root") throw new Exception("root fehlt");
            var nodes = doc.Root.Elements("data").ToList();
            if (nodes.Count != entries.Count) throw new Exception("Falsche data-Anzahl");
            var keys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var node in nodes)
            {
                string key = node.Attribute("name")?.Value ?? throw new Exception("name fehlt");
                if (!keys.Add(key) || !entries.ContainsKey(key)) throw new Exception("Doppelter/unbekannter Schlüssel: " + key);
                if (node.Element("value") is null || node.Element("comment") is null) throw new Exception("value/comment fehlt: " + key);
            }
            if (doc.Root.Elements("resheader").Count() != 4) throw new Exception("Falsche Anzahl resheader");
        }
        catch (Exception ex) { audit.Error("RESX_ROUNDTRIP", "", file.Locale, ex.Message); }
    }

    private static string ResourceDigest(List<OutputFile> output)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var file in output.OrderBy(f => f.Name, StringComparer.Ordinal))
        {
            hash.AppendData(Utf8NoBom.GetBytes(file.Name + "\n"));
            hash.AppendData(file.Bytes);
        }
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    private static string BuildManifest(Options options, List<OutputFile> output, string masterHash, string digest, Audit audit)
    {
        var manifest = new JsonObject
        {
            ["format"] = "bookstitch.localization.export-manifest.v1",
            ["exportedAtUtc"] = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["mode"] = options.Mode,
            ["masterFileSha256"] = masterHash,
            ["resourceInputsSha256"] = digest,
            ["exporterVersion"] = ExporterVersion,
            ["files"] = new JsonArray(output.Select(f => (JsonNode?)new JsonObject
            {
                ["locale"] = f.Locale,
                ["fileName"] = f.Name,
                ["sha256"] = Sha(f.Bytes),
                ["bytes"] = f.Bytes.Length
            }).ToArray()),
            ["validation"] = new JsonObject
            {
                ["result"] = "passed",
                ["errors"] = audit.Errors,
                ["warnings"] = audit.Warnings,
                ["fallbackCount"] = audit.Messages.Count(m => m.Code == "DEV_FALLBACK"),
                ["staleTranslationCount"] = audit.Messages.Count(m => m.Code is "BASELINE_STALE" or "SOURCE_HASH_STALE" or "BASELINE_MISSING"),
                ["pendingUiFitCount"] = audit.Messages.Count(m => m.Code == "UI_FIT_PENDING"),
                ["unresolvedReviewCount"] = audit.Messages.Count(m => m.Code == "REVIEW_OPEN")
            }
        };
        return manifest.ToJsonString(PrettyJson) + "\n";
    }

    private static void Publish(string stage, string dest, IEnumerable<string> names)
    {
        // Roll back ALL changed files if any filesystem operation fails. Manifest is last.
        var originals = new Dictionary<string, byte[]?>(StringComparer.OrdinalIgnoreCase);
        var touched = new List<string>();
        try
        {
            foreach (string name in names)
            {
                string target = Path.Combine(dest, name);
                byte[] incoming = File.ReadAllBytes(Path.Combine(stage, name));
                if (File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(incoming)) continue;
                originals[name] = File.Exists(target) ? File.ReadAllBytes(target) : null;
                string temp = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllBytes(temp, incoming);
                    touched.Add(name);
                    File.Move(temp, target, true);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
        }
        catch
        {
            foreach (string name in touched.AsEnumerable().Reverse())
            {
                string target = Path.Combine(dest, name);
                if (originals[name] is byte[] bytes) File.WriteAllBytes(target, bytes);
                else if (File.Exists(target)) File.Delete(target);
            }
            throw;
        }
    }

    private static int VerifyExport(List<OutputFile> files, Options options, Audit audit, string masterHash, string digest)
    {
        string manifestPath = Path.Combine(options.ResourcesPath, ManifestName);
        if (!File.Exists(manifestPath)) audit.Error("MISSING_MANIFEST", "", "", "Exportmanifest fehlt.");
        else
        {
            var manifest = JsonNode.Parse(File.ReadAllText(manifestPath, Utf8NoBom)) as JsonObject;
            if (manifest is null || OptionalStr(manifest["masterFileSha256"]) != masterHash ||
                OptionalStr(manifest["resourceInputsSha256"]) != digest ||
                OptionalStr(manifest["exporterVersion"]) != ExporterVersion)
                audit.Error("MANIFEST_STALE", "", "", "Manifest passt nicht zum aktuellen Master/Exporter.");
            if (manifest?["files"] is JsonArray arr)
            {
                var listed = arr.OfType<JsonObject>().ToDictionary(f => Str(f["fileName"], "manifest file"), StringComparer.OrdinalIgnoreCase);
                foreach (var f in files)
                {
                    if (!listed.TryGetValue(f.Name, out var m) || OptionalStr(m["sha256"]) != Sha(f.Bytes))
                        audit.Error("MANIFEST_FILE_MISMATCH", "", f.Locale, "Manifest-Eintrag stimmt nicht.");
                }
            }
            else audit.Error("MANIFEST_INVALID", "", "", "Dateiliste im Manifest fehlt.");
        }
        foreach (var f in files)
        {
            string path = Path.Combine(options.ResourcesPath, f.Name);
            if (!File.Exists(path) || !File.ReadAllBytes(path).AsSpan().SequenceEqual(f.Bytes))
                audit.Error("RESX_DRIFT", "", f.Locale, "RESX-Datei fehlt oder weicht vom Language Master ab.");
        }
        WriteReport(options, audit, audit.Errors == 0 ? "verified" : "failed", files);
        Console.WriteLine(audit.Errors == 0 ? "OK: Manifest und alle generierten RESX-Dateien stimmen." : $"FEHLER: {audit.Errors} Abweichungen.");
        return audit.Errors == 0 ? 0 : 2;
    }

    private static void PrintSummary(List<OutputFile> files, Audit audit)
    {
        Console.WriteLine($"\n--- PRUEFUEBERSICHT ---");
        Console.WriteLine($"Sprachen: {files.Count} | Fehler: {audit.Errors} | Warnungen: {audit.Warnings}");
        foreach (var group in audit.Messages.GroupBy(x => (x.Severity, x.Code))
                     .OrderByDescending(g => g.Key.Severity == "error").ThenBy(g => g.Key.Code))
            Console.WriteLine($"  {group.Key.Severity.ToUpperInvariant(),-7} {group.Key.Code,-29} {group.Count(),5}");
        if (audit.Errors > 0)
            Console.WriteLine("BEWERTUNG: NICHT VERWENDBAR fuer diesen Exportmodus. Export wird abgebrochen bzw. Verifikation schlaegt fehl.");
        else if (audit.Warnings > 0)
            Console.WriteLine("BEWERTUNG: Technisch zulaessig in diesem Modus; fachliche Warnungen sind offen. Keine sprachliche Release-Freigabe.");
        else
            Console.WriteLine("BEWERTUNG: Keine technischen Fehler oder erfassten Warnungen. Das ersetzt keine Sicht- oder Sprachpruefung.");
        Console.WriteLine("Einzelfaelle: reports/BookStitch.LocalizationValidation.json (nach abgeschlossenem Lauf)");
        Console.WriteLine("-----------------------\n");
    }

    private static void WriteReport(Options options, Audit audit, string result, List<OutputFile> files)
    {
        string reportDir = Path.Combine(options.StateDirectory, "reports");
        Directory.CreateDirectory(reportDir);
        string report = Path.Combine(reportDir, "BookStitch.LocalizationValidation.json");
        var counts = audit.Messages.GroupBy(x => (x.Severity, x.Code))
            .Select(g => new { severity = g.Key.Severity, code = g.Key.Code, count = g.Count() })
            .OrderBy(x => x.severity).ThenBy(x => x.code).ToArray();
        var obj = new
        {
            result,
            mode = options.Mode,
            exporterVersion = ExporterVersion,
            summary = new { errors = audit.Errors, warnings = audit.Warnings, files = files.Count },
            usableForMode = result != "failed" && audit.Errors == 0,
            releaseReady = false,
            releaseReadiness = "not-assessed-by-exporter",
            warningCounts = counts,
            diagnostics = audit.Messages
        };
        File.WriteAllText(report, JsonSerializer.Serialize(obj, PrettyJson) + "\n", Utf8NoBom);
        string verdict = audit.Errors > 0 ? "ABGEBROCHEN / NICHT VERWENDBAR" :
            (options.Mode == "release" && result == "exported" && audit.Warnings == 0 ? "RELEASE-EXPORT TECHNISCH OK" :
            (audit.Warnings > 0 ? "TECHNISCH OK MIT OFFENEN WARNUNGEN" : "TECHNISCH OK"));
        var lines = new List<string> { "BookStitch Localization - Ergebnis", "Modus: " + options.Mode,
            "Ergebnis: " + result, "Bewertung: " + verdict,
            $"Fehler: {audit.Errors}; Warnungen: {audit.Warnings}; Sprachen: {files.Count}",
            "Wichtiger Hinweis: Ein erfolgreicher Development-Export ist keine sprachliche Release-Freigabe.", "", "Befunde nach Kategorie:" };
        lines.AddRange(counts.Select(x => $"{x.severity}: {x.code} = {x.count}"));
        lines.Add("\nAlle Einzelfaelle: BookStitch.LocalizationValidation.json");
        File.WriteAllLines(Path.Combine(reportDir, "BookStitch.LocalizationValidation.txt"), lines, Utf8NoBom);
        if (options.CopyReportsToResources && options.Mode is "development" or "release" or "verify")
        {
            Directory.CreateDirectory(options.ResourcesPath);
            // Publish both copies together, with rollback on a filesystem failure.
            Publish(reportDir, options.ResourcesPath,
                new[] { "BookStitch.LocalizationValidation.json", "BookStitch.LocalizationValidation.txt" });
        }
        Console.WriteLine("ERGEBNIS: " + verdict);
        Console.WriteLine("BERICHT: " + report);
    }
}
