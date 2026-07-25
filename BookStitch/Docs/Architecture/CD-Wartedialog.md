# CD-Wartedialog: Legacy- und Boxed-Implementierung

## Verbindliche Architekturentscheidung

BookStitch enthält absichtlich zwei kompilierbare Darstellungen des CD-Wartedialogs:

- `DiscWaitDialogService`: bewährter Legacy-Fallback
- `BoxedDiscWaitDialogService` mit `BoxedDiscWaitDialog`: neue alternative Darstellung

Beide verwenden dieselben produktiven Pollingservices und dieselben Ergebnisobjekte.
Die zentrale Auswahl erfolgt über `SwitchableDiscWaitDialogService` und die
Entwicklereinstellung `UseBoxedDiscWaitDialog`.

## Kein Dead Code

Die Legacy-Implementierung darf nicht als doppelter oder ungenutzter Code entfernt,
zusammengeführt oder automatisch bereinigt werden. Sie bleibt als sofortiger Rückfall
erhalten, bis ausdrücklich eine spätere Entfernung beschlossen wird.

## Änderungsgrenzen

Änderungen an einer Darstellung dürfen nicht gleichzeitig Polling, Disc-Erkennung,
Duplikatprüfung, Auswurf, Import, Ripping oder Resume verändern. Beide Varianten
müssen mit Dialogsimulation und hybridem Hardwaretest geprüft werden.
