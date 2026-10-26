using BookStitch.Models;
using BookStitch.Services;
using System.Globalization;
using Xunit;

namespace BookStitch.Tests.Services;

public sealed class SourceMetadataAlternativeServiceTests
{
    [Fact]
    public void Build_CountsCompleteOrderedListVariantsRatherThanIndividualItems()
    {
        var snapshots = new[]
        {
            Snapshot(["Ada", "Berta"]),
            Snapshot(["Ada", "Berta"]),
            Snapshot(["Berta", "Ada"])
        };

        var alternatives = new SourceMetadataAlternativeService().Build(snapshots);
        var authors = alternatives[ProjectMetadataField.Authors].Cast<ListSourceMetadataAlternative>().ToArray();

        Assert.Equal(2, authors.Length);
        Assert.Contains(authors, item => item.Values.SequenceEqual(["Ada", "Berta"]) && item.SourceCount == 2);
        Assert.Contains(authors, item => item.Values.SequenceEqual(["Berta", "Ada"]) && item.SourceCount == 1);
    }

    [Fact]
    public void Build_UsesSameSequenceGroupingAsTitleCandidates()
    {
        var snapshots = Enumerable.Range(1, 16)
            .Select(index => new SourceMetadataSnapshot
            {
                SourceId = Guid.NewGuid(),
                CanonicalFields = new SourceMetadataCanonicalFields
                {
                    Title = $"{index:00} S Ánti Gundula",
                    Album = "Anderes Album"
                }
            })
            .ToArray();

        var service = new SourceMetadataAlternativeService();
        var alternatives = service.Build(snapshots);
        var title = Assert.Single(alternatives[ProjectMetadataField.Title]);
        var candidate = Assert.Single(
            service.BuildScalarTextCandidates(snapshots, ProjectMetadataField.Title));

        Assert.Equal("S Ánti Gundula", title.DisplayValue);
        Assert.Equal(16, title.SourceCount);
        Assert.Equal(title.DisplayValue, candidate.DisplayValue);
        Assert.Equal(title.SourceCount, candidate.SourceCount);
    }

    [Fact]
    public void Build_ExposesOnlyClearRepeatedProjectTitleEvidence()
    {
        var snapshots = new[]
        {
            TitleSnapshot("Billy Summers"),
            TitleSnapshot("Billy Summers"),
            TitleSnapshot("001 Billy Summers")
        };

        var alternatives = new SourceMetadataAlternativeService().Build(snapshots);
        var title = Assert.Single(alternatives[ProjectMetadataField.Title]);

        Assert.Equal("Billy Summers", title.DisplayValue);
        Assert.Equal(2, title.SourceCount);
    }

    [Fact]
    public void BuildScalarTextCandidates_ExposesNormalizedTitleAndAlbumCandidatesWithSourceCounts()
    {
        var snapshots = new[]
        {
            new SourceMetadataSnapshot
            {
                SourceId = Guid.NewGuid(),
                CanonicalFields = new SourceMetadataCanonicalFields
                {
                    Title = " Der Baum – Hörbuch ",
                    Album = "Der Baum"
                }
            },
            new SourceMetadataSnapshot
            {
                SourceId = Guid.NewGuid(),
                CanonicalFields = new SourceMetadataCanonicalFields
                {
                    Title = "Der Baum - Hörbuch",
                    Album = "Der Baum"
                }
            },
            new SourceMetadataSnapshot
            {
                SourceId = Guid.NewGuid(),
                CanonicalFields = new SourceMetadataCanonicalFields
                {
                    Title = "Kapitel 1",
                    Album = "Der Baum Deluxe"
                }
            }
        };
        var service = new SourceMetadataAlternativeService();

        var titleCandidates = service.BuildScalarTextCandidates(snapshots, ProjectMetadataField.Title);
        var albumCandidates = service.BuildScalarTextCandidates(snapshots, ProjectMetadataField.Album);

        Assert.Collection(
            titleCandidates,
            candidate =>
            {
                Assert.Equal("Der Baum - Hörbuch", candidate.DisplayValue);
                Assert.Equal(2, candidate.SourceCount);
                Assert.True(candidate.IsAutomaticWinner);
            },
            candidate =>
            {
                Assert.Equal("Kapitel 1", candidate.DisplayValue);
                Assert.Equal(1, candidate.SourceCount);
                Assert.False(candidate.IsAutomaticWinner);
            });
        Assert.Collection(
            albumCandidates,
            candidate =>
            {
                Assert.Equal("Der Baum", candidate.DisplayValue);
                Assert.Equal(2, candidate.SourceCount);
                Assert.True(candidate.IsAutomaticWinner);
            },
            candidate =>
            {
                Assert.Equal("Der Baum Deluxe", candidate.DisplayValue);
                Assert.Equal(1, candidate.SourceCount);
                Assert.False(candidate.IsAutomaticWinner);
            });
    }


    [Fact]
    public void BuildStringListItemCandidates_ExposesComposerValuesThroughGenericStringListPath()
    {
        var snapshots = new[]
        {
            new SourceMetadataSnapshot
            {
                SourceId = Guid.NewGuid(),
                CanonicalFields = new SourceMetadataCanonicalFields
                {
                    Composers = ["Ada Composer"],
                    Narrators = ["Paul Sommer"]
                }
            },
            new SourceMetadataSnapshot
            {
                SourceId = Guid.NewGuid(),
                CanonicalFields = new SourceMetadataCanonicalFields
                {
                    Composers = ["Ada Composer", "B Composer"],
                    Narrators = ["Paul Sommer"]
                }
            }
        };

        var candidates = new SourceMetadataAlternativeService()
            .BuildStringListItemCandidates(snapshots, ProjectMetadataField.Composers);

        Assert.Collection(
            candidates,
            candidate =>
            {
                Assert.Equal("Ada Composer", candidate.DisplayValue);
                Assert.Equal(2, candidate.SourceCount);
            },
            candidate =>
            {
                Assert.Equal("B Composer", candidate.DisplayValue);
                Assert.Equal(1, candidate.SourceCount);
            });
        Assert.DoesNotContain(candidates, candidate => candidate.DisplayValue == "Paul Sommer");

        var genuineComposerSnapshots = snapshots.Append(new SourceMetadataSnapshot
        {
            SourceId = Guid.NewGuid(),
            CanonicalFields = new SourceMetadataCanonicalFields
            {
                Composers = ["Paul Sommer"],
                Narrators = ["Paul Sommer"]
            }
        }).ToArray();
        var genuineComposerCandidates = new SourceMetadataAlternativeService()
            .BuildStringListItemCandidates(genuineComposerSnapshots, ProjectMetadataField.Composers);
        Assert.Contains(genuineComposerCandidates, candidate => candidate.DisplayValue == "Paul Sommer");
    }

    [Fact]
    public void BuildScalarTextCandidates_CollapsesTrackNumberPrefixesIntoOneTitleCandidate()
    {
        var snapshots = Enumerable.Range(1, 235)
            .Select(index => TitleSnapshot($"{index:000} Die Waechter"))
            .ToArray();

        var title = Assert.Single(
            new SourceMetadataAlternativeService()
                .BuildScalarTextCandidates(snapshots, ProjectMetadataField.Title));

        Assert.Equal("Die Waechter", title.DisplayValue);
        Assert.Equal(235, title.SourceCount);
    }

    [Fact]
    public void BuildScalarTextCandidates_KeepsSeparateNumberedTitleFamiliesAsSeparateCandidates()
    {
        var snapshots = new[]
        {
            TitleSnapshot("001 Alpha"),
            TitleSnapshot("002 Alpha"),
            TitleSnapshot("001 Beta"),
            TitleSnapshot("002 Beta"),
            TitleSnapshot("003 Beta")
        };

        var candidates = new SourceMetadataAlternativeService()
            .BuildScalarTextCandidates(snapshots, ProjectMetadataField.Title);

        Assert.Collection(
            candidates,
            candidate =>
            {
                Assert.Equal("Beta", candidate.DisplayValue);
                Assert.Equal(3, candidate.SourceCount);
            },
            candidate =>
            {
                Assert.Equal("Alpha", candidate.DisplayValue);
                Assert.Equal(2, candidate.SourceCount);
            });
    }

    [Fact]
    public void BuildScalarTextCandidates_DoesNotTreatLargeYearLikePrefixesAsTrackNumbers()
    {
        var snapshots = new[]
        {
            TitleSnapshot("1984 Roman"),
            TitleSnapshot("1985 Roman")
        };

        var candidates = new SourceMetadataAlternativeService()
            .BuildScalarTextCandidates(snapshots, ProjectMetadataField.Title);

        Assert.Equal(2, candidates.Count);
        Assert.Contains(candidates, candidate => candidate.DisplayValue == "1984 Roman");
        Assert.Contains(candidates, candidate => candidate.DisplayValue == "1985 Roman");
    }

    [Fact]
    public void BuildScalarTextCandidates_DoesNotCollapseMeaningfulElevenSeptemberTitles()
    {
        var snapshots = new[]
        {
            TitleSnapshot("11. September"),
            TitleSnapshot("12. September")
        };

        var candidates = new SourceMetadataAlternativeService()
            .BuildScalarTextCandidates(snapshots, ProjectMetadataField.Title);

        Assert.Equal(2, candidates.Count);
        Assert.Contains(candidates, candidate => candidate.DisplayValue == "11. September");
        Assert.Contains(candidates, candidate => candidate.DisplayValue == "12. September");
    }

    [Fact]
    public void BuildStringListItemCandidates_GroupsPeopleByNormalizedValueAndCountsSources()
    {
        var snapshots = new[]
        {
            new SourceMetadataSnapshot
            {
                SourceId = Guid.NewGuid(),
                CanonicalFields = new SourceMetadataCanonicalFields
                {
                    Authors = [" Katharina Quast ", "Co Author"],
                    Narrators = ["David Nathan"]
                }
            },
            new SourceMetadataSnapshot
            {
                SourceId = Guid.NewGuid(),
                CanonicalFields = new SourceMetadataCanonicalFields
                {
                    Authors = ["Katharina Quast"],
                    Narrators = ["David Nathan"]
                }
            }
        };

        var service = new SourceMetadataAlternativeService();
        var authors = service.BuildStringListItemCandidates(snapshots, ProjectMetadataField.Authors);
        var narrators = service.BuildStringListItemCandidates(snapshots, ProjectMetadataField.Narrators);

        Assert.Collection(
            authors,
            candidate =>
            {
                Assert.Equal("Katharina Quast", candidate.DisplayValue);
                Assert.Equal(2, candidate.SourceCount);
                Assert.Equal(["Katharina Quast"], candidate.Values);
                Assert.Equal(MetadataOrigin.SourceAggregation, candidate.EvidenceOrigin);
                Assert.Equal("Quelldatei ★", candidate.EvidenceLabel);
            },
            candidate =>
            {
                Assert.Equal("Co Author", candidate.DisplayValue);
                Assert.Equal(1, candidate.SourceCount);
            });

        var narrator = Assert.Single(narrators);
        Assert.Equal("David Nathan", narrator.DisplayValue);
        Assert.Equal(2, narrator.SourceCount);
        Assert.All(authors, candidate => Assert.True(candidate.IsAutomaticWinner));
        Assert.True(narrator.IsAutomaticWinner);
    }

    [Fact]
    public void CandidateEvidenceLabel_UsesTheCandidateSpecificOrigin()
    {
        var candidate = new ScalarSourceMetadataAlternative(
            ProjectMetadataField.Title,
            "Der Titel",
            1,
            "Der Titel")
        {
            EvidenceOrigin = MetadataOrigin.FolderStructure,
            IsAutomaticWinner = true
        };

        Assert.Equal("Ordner ★", candidate.EvidenceLabel);
        Assert.True(candidate.IsAutomaticWinner);
        Assert.False(candidate.IsCanonicalWinner);
        Assert.Equal(1, candidate.SourceCount);
    }

    [Fact]
    public void Build_HandlesThirteenHundredCanonicalSourcesWithoutAPathologicalDelay()
    {
        var snapshots = Enumerable.Range(0, 1300)
            .Select(index => new SourceMetadataSnapshot
            {
                SourceId = Guid.NewGuid(),
                CanonicalFields = new SourceMetadataCanonicalFields
                {
                    Title = $"Kapitel {index % 40}",
                    Authors = [$"Autor {index % 7}"],
                    Narrators = [$"Stimme {index % 5}"],
                    Genres = ["Audiobook"],
                    Languages = ["de-DE"]
                }
            })
            .ToArray();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();

        var alternatives = new SourceMetadataAlternativeService().Build(snapshots);

        stopwatch.Stop();
        Assert.False(alternatives.ContainsKey(ProjectMetadataField.Title));
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"Alternativenaufbau dauerte {stopwatch.Elapsed}.");
    }

    [Fact]
    public void BuildGenreCandidates_MergesSourceAndStaticListWithoutDuplicates()
    {
        var snapshots = new[]
        {
            new SourceMetadataSnapshot
            {
                SourceId = Guid.NewGuid(),
                CanonicalFields = new SourceMetadataCanonicalFields { Genres = ["Krimi", "Fantasy"] }
            },
            new SourceMetadataSnapshot
            {
                SourceId = Guid.NewGuid(),
                CanonicalFields = new SourceMetadataCanonicalFields { Genres = ["Krimi"] }
            }
        };

        var candidates = new SourceMetadataAlternativeService().BuildGenreCandidates(
            snapshots,
            ["Krimi", "Biografie"]);

        Assert.Collection(
            candidates,
            krimi =>
            {
                Assert.Equal("Krimi", krimi.DisplayValue);
                Assert.Equal(2, krimi.SourceCount);
                Assert.Equal(MetadataOrigin.SourceAggregation, krimi.EvidenceOrigin);
                Assert.True(krimi.IsAutomaticWinner);
                Assert.Equal("Quelldatei ★", krimi.EvidenceLabel);
            },
            fantasy =>
            {
                Assert.Equal("Fantasy", fantasy.DisplayValue);
                Assert.Equal(1, fantasy.SourceCount);
                Assert.Equal(MetadataOrigin.SourceAggregation, fantasy.EvidenceOrigin);
                Assert.True(fantasy.IsAutomaticWinner);
                Assert.Equal("Quelldatei ★", fantasy.EvidenceLabel);
            },
            biography =>
            {
                Assert.Equal("Biografie", biography.DisplayValue);
                Assert.Equal(1, biography.SourceCount);
                Assert.Equal(MetadataOrigin.StaticList, biography.EvidenceOrigin);
                Assert.False(biography.IsAutomaticWinner);
                Assert.False(biography.ShowSourceCount);
                Assert.Equal("Standardliste", biography.EvidenceLabel);
            });
    }

    [Fact]
    public void WorkFields_ExposeCanonicalSubtitleVolumeEditionAndEnumCandidates()
    {
        var snapshots = new[]
        {
            new SourceMetadataSnapshot
            {
                SourceId = Guid.NewGuid(),
                CanonicalFields = new SourceMetadataCanonicalFields
                {
                    Subtitle = "Untertitel",
                    Volume = "Band 3",
                    Edition = "Ausgabe A",
                    AudioWorkType = AudioWorkType.AudioDrama,
                    AbridgedStatus = AbridgedStatus.Unabridged
                }
            },
            new SourceMetadataSnapshot
            {
                SourceId = Guid.NewGuid(),
                CanonicalFields = new SourceMetadataCanonicalFields
                {
                    Subtitle = "Untertitel",
                    Volume = "Band 3",
                    Edition = "Ausgabe B",
                    AudioWorkType = AudioWorkType.AudioDrama,
                    AbridgedStatus = AbridgedStatus.Unabridged
                }
            }
        };

        var service = new SourceMetadataAlternativeService();

        Assert.Equal(2, Assert.Single(service.BuildScalarTextCandidates(snapshots, ProjectMetadataField.Subtitle)).SourceCount);
        Assert.Equal(2, Assert.Single(service.BuildScalarTextCandidates(snapshots, ProjectMetadataField.Volume)).SourceCount);
        Assert.Equal("Ausgabe A", service.BuildScalarTextCandidates(snapshots, ProjectMetadataField.Edition)[0].DisplayValue);

        var all = service.Build(snapshots);
        var workType = Assert.Single(all[ProjectMetadataField.AudioWorkType]);
        var abridged = Assert.Single(all[ProjectMetadataField.AbridgedStatus]);
        Assert.Equal(AudioWorkType.AudioDrama, Assert.IsType<ScalarSourceMetadataAlternative>(workType).Value);
        Assert.Equal(AbridgedStatus.Unabridged, Assert.IsType<ScalarSourceMetadataAlternative>(abridged).Value);
    }

    [Fact]
    public void Build_SeriesCandidatesPreserveNamePartPairsAndEntryOrder()
    {
        var snapshots = new[]
        {
            new SourceMetadataSnapshot
            {
                SourceId = Guid.NewGuid(),
                CanonicalFields = new SourceMetadataCanonicalFields
                {
                    Series = [new SeriesEntry("Saga", "2"), new SeriesEntry("Nebenreihe", "A")]
                }
            },
            new SourceMetadataSnapshot
            {
                SourceId = Guid.NewGuid(),
                CanonicalFields = new SourceMetadataCanonicalFields
                {
                    Series = [new SeriesEntry("Saga", "2"), new SeriesEntry("Nebenreihe", "A")]
                }
            },
            new SourceMetadataSnapshot
            {
                SourceId = Guid.NewGuid(),
                CanonicalFields = new SourceMetadataCanonicalFields
                {
                    Series = [new SeriesEntry("Saga", "3")]
                }
            }
        };

        var alternatives = new SourceMetadataAlternativeService().Build(snapshots);
        var series = alternatives[ProjectMetadataField.Series]
            .Cast<SeriesSourceMetadataAlternative>()
            .ToArray();

        Assert.Equal(2, series.Length);
        var repeated = Assert.Single(series, item => item.SourceCount == 2);
        Assert.Equal(
            [new SeriesEntry("Saga", "2"), new SeriesEntry("Nebenreihe", "A")],
            repeated.Entries);
        Assert.Equal("Saga #2; Nebenreihe #A", repeated.DisplayValue);
    }

    [Fact]
    public void CandidateEvidenceLabel_AppendsWinnerMarkerOnlyForWinners()
    {
        var normal = new ListSourceMetadataAlternative(
            ProjectMetadataField.Genres,
            ["Krimi"],
            3,
            "Krimi")
        {
            EvidenceOrigin = MetadataOrigin.SourceAggregation
        };
        var canonicalOnly = normal with { IsCanonicalWinner = true };
        var automaticWinner = normal with { IsAutomaticWinner = true };

        Assert.Equal("Quelldatei", normal.EvidenceLabel);
        Assert.Equal("Quelldatei", canonicalOnly.EvidenceLabel);
        Assert.Equal("Quelldatei ★", automaticWinner.EvidenceLabel);
    }

    private static SourceMetadataSnapshot TitleSnapshot(string title) => new()
    {
        SourceId = Guid.NewGuid(),
        CanonicalFields = new SourceMetadataCanonicalFields { Title = title }
    };

    private static SourceMetadataSnapshot Snapshot(IReadOnlyList<string> authors) => new()
    {
        SourceId = Guid.NewGuid(),
        CanonicalFields = new SourceMetadataCanonicalFields { Authors = authors }
    };
    [Fact]
    public void Field_chrome_origin_format_is_optional_without_changing_candidate_evidence_format()
    {
        Assert.Equal(string.Empty, MetadataOriginEvidenceLabels.FormatOptional(MetadataOrigin.None));
        Assert.Equal("Quelldatei", MetadataOriginEvidenceLabels.FormatOptional(MetadataOrigin.SourceAggregation));
        Assert.Equal("–", MetadataOriginEvidenceLabels.Format(MetadataOrigin.None));
    }

    [Fact]
    public void OriginAndEnumPresentationUseTheActiveUiCulture()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("es-ES");
            Assert.Equal("Archivo de origen", MetadataOriginEvidenceLabels.Format(MetadataOrigin.SourceAggregation));
            Assert.Equal("Derivado", MetadataOriginEvidenceLabels.Format(MetadataOrigin.Derived));
            Assert.Equal("Audiolibro", MetadataDisplayLabels.AudioWorkType(AudioWorkType.Audiobook));
            Assert.Equal("Abreviado", MetadataDisplayLabels.AbridgedStatus(AbridgedStatus.Abridged));
        }
        finally
        {
            CultureInfo.CurrentUICulture = previous;
        }
    }

}
