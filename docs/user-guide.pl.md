# Wenta — podręcznik użytkownika (polski)

Ten podręcznik opisuje to, co istnieje w repozytorium dzisiaj: bibliotekę
C# `Wenta.Core` oraz wtyczkę `WentaZwcad` dla ZWCAD 2021. Wszystko poniżej
pochodzi z kodu źródłowego, plików README i zestawu testów; polecenia lub
funkcje dopiero planowane wymieniono w punkcie „Plan rozwoju”, a nie
opisano jako istniejące.

Wersja angielska: `docs/user-guide.en.md`.

---

## 1. Czym jest Wenta

Wenta to zestaw narzędzi inżynierskich dla instalacji wentylacyjnych
(kanały wentylacyjne), napisany w C#:

- **`Wenta.Core`** (`csharp/Wenta.Core`, przestrzeń nazw `Wenta`) —
  biblioteka .NET bez zależności zewnętrznych: dobór przekrojów kanałów,
  straty liniowe i miejscowe, obliczanie sieci ze ścieżką krytyczną,
  regulacja (równoważenie), akustyka, wentylatory, izolacja, bilans
  powietrza w pomieszczeniach, zestawienie materiałów z kodami KNR, otwarty
  format katalogu współczynników ζ, zapis/odczyt sieci w JSON, rozpoznawanie
  topologii z polilinii, wykrywanie kolizji i wielkości warsztatowe.
  Docelowa platforma: .NET Framework 4.x; kompilacja zwykłym `csc.exe`
  (bez NuGet, bez projektów SDK).
- **`WentaZwcad`** (`zwcad-plugin/`) — wtyczka do ZWCAD 2021. Każdy plik
  `Wenta.Core` jest wkompilowany *do* `WentaZwcad.dll`, więc wdrożenie to
  jedna biblioteka DLL, plik wstęgi (`Wenta.CUIX`) i jeden przykładowy
  katalog. W czasie działania nie ma Pythona, Mojo, Rusta ani WASM.

Wszystkie wielkości w bibliotece są w SI: metry, m²/m³, m³/s, Pa, m/s, W,
kg. Katalogi i tablice wymiarów używają milimetrów tam, gdzie to zaznaczono.
`Units.cs` zawiera przeliczniki na jednostki anglosaskie (`CfmToM3s`,
`InwcToPa`, `FtToM`, `InToM`, `FpmToMs`, `FToC` i odwrotne).

Licencja: MIT. Repozytorium: <https://github.com/ModelTok/wenta>.

---

## 2. Instalacja wtyczki

**Wymaganie: ZWCAD 2021, wersja 64-bitowa.** Wtyczka łączy się z
`ZwManaged.dll` i `ZwDatabaseMgd.dll` z katalogu
`C:\Program Files\ZWSOFT\ZWCAD 2021` i jest kompilowana z `/platform:x64`.
Inne wersje ZWCAD nie są obsługiwane przez skrypt budowania ani instalator
rejestru.

### 2.1 Wymagania do budowania

- Visual Studio 2022 Build Tools (lub dowolna edycja VS 2022; `csc.exe`
  jest wyszukiwany przez `vswhere`).
- .NET Framework 4.x (kompilacja odwołuje się do `System.dll`,
  `System.Core.dll`, `System.Windows.Forms.dll`, `System.Drawing.dll`,
  `System.Web.Extensions.dll`).
- ZWCAD 2021 zainstalowany w domyślnej lokalizacji (biblioteki API
  zarządzanego).
- Opcjonalnie `just` do przepisów z głównego `justfile`.

### 2.2 Budowanie

```cmd
just csharp-parity      # csharp\build.cmd + uruchomienie Wenta.Core.Tests.exe
just zwcad-build        # zwcad-plugin\build.cmd -> bin\WentaZwcad.dll + Wenta.CUIX
```

`zwcad-plugin\build.cmd` tworzy `WentaZwcad.dll`, kopiuje obok niej
`csharp\catalogs\example-generic.json` i buduje `Wenta.CUIX` narzędziem
`tools\MakeCuix.cs`. Najpierw uruchom testy biblioteki; program testowy
kończy się wierszem `==== N passed, 0 failed ====`.

### 2.3 Instalacja (`install.ps1`, z uprawnieniami administratora)

```powershell
powershell -ExecutionPolicy Bypass -File install.ps1
```

Skrypt odmawia działania bez uprawnień administratora (zapisuje do HKLM).
Następnie:

1. kopiuje `WentaZwcad.dll`, `Wenta.CUIX` i `example-generic.json` z katalogu
   wyjściowego kompilacji do `C:\ProgramData\WentaZwcad\`;
2. tworzy klucz `HKLM\SOFTWARE\ZWSOFT\ZWCAD\2021\en-US\Applications\WentaZwcad`
   z wartościami `LOADCTRLS = 14` (start + na polecenie + ręcznie),
   `MANAGED = 1`, `LOADER = C:\ProgramData\WentaZwcad\WentaZwcad.dll`
   oraz `DESCRIPTION`.

Uwaga: `install.ps1` pobiera trzy pliki z katalogu `bin` obok skryptu
(`zwcad-plugin\bin`, czyli z wyniku `build.cmd`) i przerywa pracę, jeśli
brakuje tam któregokolwiek z nich.

### 2.4 Jednorazowe wczytanie wstęgi

W ZWCAD wykonaj:

```
_.MENULOAD C:\ProgramData\WentaZwcad\Wenta.CUIX
```

ZWCAD zapamiętuje częściowy plik CUIX w profilu; na wstędze pojawia się karta
**Wenta**. (W skrypcie `MENULOAD` wymaga wcześniejszego ustawienia `FILEDIA`
na `0` — patrz `full_test.scr`.)

### 2.5 Odinstalowanie (`uninstall.ps1`, z uprawnieniami administratora)

Usuwa klucz rejestru i katalog `C:\ProgramData\WentaZwcad`. Karta wstęgi
może pozostać w profilu ZWCAD do czasu usunięcia jej przez `MENULOAD`/`CUI`.

---

## 3. Opis poleceń

Istnieje czternaście poleceń. Każde dopisuje jeden wiersz do dziennika
`%TEMP%\wenta_zwcad_test.txt` (np. `WENTADUCT ok  200×200 mm  0,150 m3/s
3,75 m/s  0,9530 Pa/m`). Obliczenia doboru i spadku ciśnienia wykonuje
`Wenta.Core`; we wtyczce nie ma żadnych formuł.

### 3.1 `WENTADUCT` — dobór i narysowanie przekroju kanału

Zapytania (wartości domyślne w nawiasach ostrych):

1. `Duct shape [Round/Rectangular] <Rectangular>` — kształt: okrągły /
   prostokątny
2. `Design flow [m³/s]` — strumień obliczeniowy, domyślnie `0.1`, musi być
   dodatni
3. `Target velocity [m/s]` — prędkość docelowa, domyślnie `4.0`, musi być
   dodatnia

Polecenie wywołuje `Sizing.VelocityMethod(flow, shape, targetVelocity)`,
które zwraca najmniejszy znormalizowany przekrój wg EN 1505 (prostokątny)
lub EN 1506 (okrągły), w którym prędkość nie przekracza wartości docelowej.
Następnie rysuje w przestrzeni modelu, w początku układu:

- prostokątny: zamkniętą polilinię `szerokość × wysokość` w milimetrach;
- okrągły: okrąg;
- tekst `DBText` nad przekrojem, wysokość 40, o treści
  `<wymiar>  <strumień> m³/s  <prędkość> m/s  <Δp> Pa/m`. Wartość Pa/m to
  jednostkowy spadek ciśnienia na tarcie dla tego przekroju (współczynnik
  tarcia Swamee–Jain, chropowatość bezwzględna 0,1 mm, powietrze
  standardowe).

Obrys otrzymuje znacznik `WENTA` (XData, nazwa aplikacji `WENTA`): klasę
`duct`, znacznik schematu `v2`, identyfikator GUID, a następnie pary
klucz/wartość jako ciągi znaków (`geometry` = `section`, `shape`, `d_mm` lub
`w_mm`/`h_mm`, `flow_m3s`, `velocity_ms`), zapisane w kulturze niezmiennej.
Polecenia sieciowe (§3.6–3.11) używają tego samego układu. Rekord z wcześniejszych
wersji (`duct`, strumień, prędkość, kod kształtu, etykieta wymiaru) jest nadal
odczytywany. W wierszu poleceń pojawia się
`Sized: 200×200 mm · 0.150 m³/s · v=3.75 m/s (target 4.0) · 0.95 Pa/m`
(wartości ze zweryfikowanego przebiegu bezobsługowego dla `Rectangular`,
`0.15`, `4.0`).

### 3.2 `WENTACATALOG` — wczytanie otwartego katalogu ζ

Wczytuje `example-generic.json` z katalogu, w którym leży `WentaZwcad.dll`
(po instalacji `C:\ProgramData\WentaZwcad`). Wypisuje nazwę i wersję
katalogu, liczbę pozycji (5 w dostarczonym pliku) oraz przykładowe
wyszukanie: `rect_elbow 400×200 → ζ=… (source: …)` przy użyciu
`ZetaCatalog.Match` i `ZetaCatalog.ZetaFor`. Gdy pliku brak, wypisuje
`No example catalog beside the DLL: <ścieżka>`; uszkodzony plik daje
`Catalog load failed: <komunikat>`. Polecenie nie czyta rysunku.

### 3.3 `WENTABOM` — obliczenie sieci wzorcowej i eksport zestawienia

To polecenie **nie** czyta rysunku. Buduje stałą sieć wzorcową z trójnikiem,
tę samą, której używają wektory parzystości:

```
Centrala -> kanał okrągły D315, 20 m -> trójnik (ζ przelot 0,1, ζ odgałęzienie 0,4)
      -> przelot:       kanał okrągły D200, 5 m -> nawiewnik 0,06 m³/s
      -> odgałęzienie:  przewód elastyczny D125, 3 m, 2 Pa/m -> nawiewnik 0,04 m³/s
```

Rozwiązuje ją (`Network.Solve`, ścieżka krytyczna **7,63 Pa**; wartość z
wektora testowego to 7,629497821799035 Pa), buduje `Bom.Build(net)`
(7 wierszy, łączna długość kanałów 28,0 m) i zapisuje `bom.ToCsv()` do
**`%TEMP%\wenta_bom.csv`**. Plik CSV jest rozdzielany średnikami, z kropką
dziesiętną:

```
item;kind;description;length_m;area_m2;flow_m3s;knr_code
```

Kolumna `knr_code` zawiera wbudowane kody zastępcze (`KNR 2-08 0101
(configure)` itd.). Kolumna `area_m2` to — zgodnie z implementacją w `Bom` —
pole przekroju × długość; powierzchnię blachy daje osobno
`Fabrication.DuctSurfaceAreaM2`.

### 3.4 `WENTAPANEL` — dokowalna paleta doboru

Otwiera przyczepialną paletę **Wenta** (minimum 240 × 260 px) z polami:

- `Flow [m³/s]` — pole liczbowe, od 0,001 do 20, domyślnie 0,1;
- `Target v [m/s]` — lista `2.0 2.5 3.0 4.0 5.0 6.0 7.5`, domyślnie 4,0;
- `Shape` — `Rectangular` (domyślnie) lub `Round`;
- podgląd na żywo przekroju, jaki wybrałaby `VelocityMethod`, np.
  `→ rect 200x200mm   v = 3.75 m/s`;
- **Draw sized duct section** — wysyła do aktywnego rysunku polecenie
  `WENTADUCT` z gotowymi odpowiedziami;
- **Plugin info** — uruchamia `WENTAHELLO`.

Paleta nie otwiera się już automatycznie przy starcie; użyj polecenia lub
przycisku na wstędze.

### 3.5 `WENTAHELLO` — informacje o wtyczce

Wypisuje `Wenta duct plugin (wenta C# core, MIT). ZWCAD version: <wersja>`
i zapisuje wersję ZWCAD do dziennika (zweryfikowano: `21.10.21.0`).

### 3.6 Rysowanie sieci dla poleceń sieciowych

`WENTATRACE`, `WENTAPRESSURE`, `WENTASIZE` i `WENTABALANCE` odczytują sieć
z rysunku. Należy ją narysować następująco (jednostki: milimetry):

- **Osie kanałów** — jeden `LINE`, otwarta `LWPOLYLINE` lub `ARC` na każdy
  odcinek. Odcinki łączą się we wspólnych punktach końcowych (tolerancja
  przyciągania 1 mm). Punkt, w którym spotykają się trzy odcinki, staje się
  trójnikiem; cztery lub więcej odcinków w jednym punkcie oraz zamknięte pętle
  są odrzucane. Łuki i wybrzuszenia polilinii są odwzorowywane cięciwami
  o kącie nie większym niż 5°. Zamknięte polilinie (np. obrys z `WENTADUCT`)
  są pomijane.
- **Znaczniki** — wstawiane poleceniem `WENTATAG` (§3.7): jedno *źródło*
  przy końcu centrali, jeden *nawiewnik/wywiewnik* (terminal) ze strumieniem
  obliczeniowym przy każdym pozostałym otwartym końcu oraz opcjonalnie
  *przepustnice* na odgałęzieniach. Znacznik terminala musi leżeć nie dalej
  niż 150 mm od otwartego końca; otwarty koniec bez strumienia przerywa
  polecenie, które podaje położenia do oznaczenia.
- **Wymiary** — odcinek przyjmuje przekrój ze znacznika `WENTA` zapisanego
  przez `WENTASIZE` (lub starszego rekordu `WENTADUCT`); odcinki bez
  znacznika mają Ø200.

W każdym trójniku odnoga, która przedłuża kierunek odcinka dopływowego, jest
przelotem (ζ = 0), a druga odgałęzieniem (ζ = 0,5), więc kolejność i kierunek
rysowania linii nie mają znaczenia. Gdy polecenie prosi o obiekty, należy
zaznaczyć całą sieć razem ze znacznikami (działa `ALL`: etykiety i nakładki
są pomijane).

### 3.7 `WENTATAG` — znacznik terminala, źródła lub przepustnicy

Zapytania: `Marker [Terminal/Source/Damper] <Terminal>`, położenie, a dla
terminala `Terminal design flow [m³/s] <0.1>`. Polecenie rysuje okrąg Ø200 na
warstwie `WENTA-TAG` z etykietą (`T 0.060 m³/s`, `SRC` lub `DMP`) i zapisuje
na nim znacznik `WENTA`. Ponowne oznaczenie w promieniu 150 mm od istniejącego
znacznika zmienia ten znacznik (klasę, strumień, położenie) zamiast dodawać
drugi i usuwa jego poprzedni wynik równoważenia.

### 3.8 `WENTATRACE` — budowa sieci z rysunku

Buduje `Wenta.Network` z zaznaczenia (`DrawingTrace.Build` na bazie
`Topology.Trace`), rozwiązuje ją dla powietrza standardowego i wypisuje
liczbę odcinków, trójników i terminali, łączną długość kanałów, strumień przy
źródle, spadek ciśnienia na drodze krytycznej oraz po jednym wierszu na
odcinek (`duct0  5.00 m  Ø200  0.100 m³/s`). Sieć jest zapisywana do
**`%TEMP%\wenta_trace.json`** (Network JSON, §5.3); elementy pochodzące
z oznaczonych obiektów mają identyfikator GUID tego obiektu.

### 3.9 `WENTAPRESSURE` — spadek ciśnienia na drodze krytycznej

Pyta o wysokość położenia (`<0>` m; powyżej 0 gęstość powietrza wynika
z `Fluid.AirAtAltitude`). Wypisuje tabelę `PressureReport` drogi krytycznej
oraz tabelę strumienia, prędkości i Δp dla każdego elementu
(`Analysis.Analyze`), zapisuje raport CSV do **`%TEMP%\wenta_pressure.csv`**
i rysuje odcinki krytyczne jako polilinię o szerokości 40 mm na warstwie
`WENTA-CRIT` (czyszczonej i rysowanej od nowa przy każdym uruchomieniu).
Spadek na drodze krytycznej jest zapamiętywany w rysunku, więc kolejne
uruchomienie podaje też zmianę względem poprzedniego, np.
`previous run 4.43 Pa, change +3.57 Pa`.

### 3.10 `WENTASIZE` — dobór wszystkich odcinków sieci

Pyta o metodę i jej wartość docelową:

| Metoda | Zapytanie (domyślnie) | Funkcja biblioteki |
|---|---|---|
| `Velocity` | prędkość docelowa (4,0 m/s) | `Sizing.VelocityMethod` |
| `EqualFriction` | jednostkowy spadek ciśnienia (1,0 Pa/m) | `Sizing.EqualFrictionMethod` |
| `Budget` | dopuszczalny spadek na odcinek (10 Pa) | `Sizing.PressureDropBudget` |
| `Noise` | rodzaj pomieszczenia (`office`) | `Sizing.NoiseLimitMethod` |
| `Aspect` | prędkość docelowa (4,0 m/s), proporcja boków (2,0) | `Sizing.AspectRatioMethod` (prostokątne) |

oraz o kształt (domyślnie `Round` lub `Rectangular`). Solver rozprowadza
strumienie terminali na wszystkie odcinki, `BatchSizing.Size` dobiera każdy
odcinek i zaokrągla go do szeregu EN 1506 / EN 1505. Każda oś otrzymuje
w znaczniku `WENTA` wymiar, strumień i prędkość oraz etykietę na warstwie
`WENTA-LABEL`, np. `Ø200  0.100 m³/s  3.18 m/s`. Ponowne uruchomienie
aktualizuje te same etykiety w miejscu (uchwyt etykiety jest zapisany
w znaczniku) zamiast dodawać nowe. Na końcu podawany jest spadek ciśnienia na
drodze krytycznej sieci po doborze.

### 3.11 `WENTABALANCE` — nastawy przepustnic dla terminali

Pyta o wysokość położenia, rozwiązuje sieć i wywołuje `ReFit.Hints`: dla
każdego terminala nadwyżkę ciśnienia względem drogi krytycznej, współczynnik
ζ przepustnicy, który ją zdławi, oraz stopień otwarcia przepustnicy
jednopłaszczyznowej (`Balancing`, §4.9). Wartości są zapisywane w znacznikach
terminali i w każdym znaczniku przepustnicy na odcinku zasilającym terminal
bezpośrednio oraz pokazywane w ich etykietach
(`T 0.060 m³/s  ζ=0.15 93% open`). Odgałęzienie, którego przepustnica
musiałaby się całkowicie zamknąć, jest oznaczane `CHANGE HARDWARE` (mniejszy
kanał, kryza lub inny typ przepustnicy). Zestawienie trafia do
**`%TEMP%\wenta_balance.csv`**:

```
marker,class,terminal_id,run_id,available_dp_pa,required_dp_pa,surplus_dp_pa,damper_zeta,damper_open_percent,needs_hardware
```

### 3.12 Bezobsługowe sprawdzenie poleceń sieciowych

`zwcad-plugin/network_test.scr` rysuje układ wzorcowy (magistrala 5 m od
(0,0) do trójnika w (5000,0); przelot 3 m do terminala 0,06 m³/s;
odgałęzienie 3 m do terminala 0,04 m³/s; przepustnica na odgałęzieniu)
i uruchamia na nim wszystkie polecenia sieciowe. Najpierw trzeba wczytać
wtyczkę (zainstalowaną lub przez `NETLOAD` w skrypcie opakowującym
z `FILEDIA 0`). Zweryfikowano w ZWCAD 2021: `WENTATRACE` 4,43 Pa przy
domyślnym Ø200; po `WENTASIZE` metodą prędkości 4 m/s (magistrala Ø200,
przelot Ø150, odgałęzienie Ø125) 8,00 Pa, a drugie `WENTASIZE`
zaktualizowało trzy etykiety, nie dodając nowych; `WENTABALANCE` ζ 0,15 /
otwarcie 93 % dla terminala na przelocie, a odgałęzienie jest drogą krytyczną
i jego przepustnica pozostaje całkowicie otwarta. Następnie skrypt uruchamia
`WENTAFAN` (ciśnienie dodatkowe 150 Pa: `ExampleFan EF-160` przy 0,12 m³/s
i 160,7 Pa), dwukrotnie `WENTAINSULATE` (20 mm wełny mineralnej na każdym
odcinku; drugie uruchomienie aktualizuje trzy etykiety), `WENTASOUND`
i `WENTAHELP`. Są to wartości, które `Wenta.Core` oblicza dla tego układu.
Na koniec sprawdzana jest wymiana w obie strony: `WENTAWRITE` rysuje
odczytaną sieć w (0,−20000) (7 elementów), `WENTAREAD` odczytuje ją
z powrotem (4,43 Pa, wartość z `WENTATRACE`), drugie `WENTAWRITE`
aktualizuje 7 obiektów w miejscu, a JSON z drugiego `WENTAREAD` jest
identyczny bajt w bajt z pierwszym; po usunięciu elementów punktowych
`WENTAREAD` odmawia z powodu osieroconych znaczników, a po wydłużeniu
jednego narysowanego przewodu o 1 m zgłasza jedną zmianę i 5,14 Pa.
Na końcu `WENTAATTEN` (studio, 50 m², α 0,2, przykładowy katalog) wstawia
`ExampleAtten EA-200-1200` i `EA-200-600` przed dwoma terminalami, żadne
pomieszczenie nie przekracza limitu, a ΔP rośnie z 5,14 do 6,84 Pa;
`WENTAREAD` odczytuje potem 9 elementów i 6,84 Pa.
Na tym samym rysunku działają potem polecenia edycji: `WENTAPROPS`
`duct1` średnica 270 zostaje zaokrąglona w górę do Ø300 (6,84 → 6,12 Pa),
`WENTASTRETCH` `duct1` do 4 m przesuwa tłumik i terminal na jego końcu
(6,16 Pa), `WENTAMOVE` `tee0` o (0, −500) wydłuża trzy dołączone
przewody (6,17 Pa), a `WENTAPROPS` `term0` przepływ 0,05 m³/s daje
7,22 Pa; `WENTAREAD` odczytuje następnie 9 elementów bez zmian długości,
7,22 Pa, z niezmienionymi GUID. Na końcu rysunek jest czyszczony,
`example-dampers-network.json` zostaje narysowany w (0,−40000), a
`WENTAREFIT` `t1` 0,12 m³/s (prędkość 4 m/s) zmniejsza odgałęzienie do
Ø125, ustawia przepustnicę przelotu `dS` na ζ 0,14 przy otwartej
przepustnicy odgałęzienia `dB` i zmienia ΔP z 2,59 na 6,98 Pa bez
pozostałego niezrównoważenia w około 6 ms; `WENTAREAD` odczytuje 6,98 Pa
bez zmian.

### 3.13 `WENTAFAN` — wentylator w punkcie pracy

Należy zaznaczyć sieć (jak w §3.8). Zapytania: wysokość położenia,
`Extra pressure outside the ductwork (AHU, filters, coils) [Pa] <0>` oraz
plik katalogu wentylatorów (Enter wybiera `example-fans.json` obok DLL).
Punkt pracy to suma strumieni terminali oraz spadek ciśnienia na drodze
krytycznej powiększony o ciśnienie dodatkowe (`FanSelection.DutyPoint`).
Polecenie wypisuje każdy wentylator z katalogu z ciśnieniem, jakie daje jego
charakterystyka przy strumieniu obliczeniowym, zapasem oraz mocą Q·p/η
w punkcie pracy, jeśli wentylator go spełnia i ma podaną sprawność
(`Fan.Margin`, `Fan.Power`, §4.11). Wybrany wentylator, oznaczony `*`, to
pierwszy w kolejności katalogu, który spełnia punkt pracy (`Fan.PickFan`);
jest zapisywany w znaczniku źródła (`fan_id`, `fan_flow_m3s`,
`fan_required_pa`, `fan_margin_pa`, `fan_power_w`). Format katalogu opisuje
§5.4; przykładowe wentylatory są fikcyjne.

### 3.14 `WENTAINSULATE` — grubość izolacji odcinków

Należy zaznaczyć dobrane osie kanałów (odcinki bez wymiaru w znaczniku
`WENTA` są pomijane; najpierw trzeba uruchomić `WENTASIZE`). Zapytania: cel
`[Condensation/HeatLoss]`, materiał `[MineralWool/PeFoam/Epdm/Pir/PuFoam]`,
temperatura powietrza w kanale, temperatura otoczenia, a następnie punkt rosy
otoczenia (kondensacja) albo dopuszczalne straty ciepła w W/m. Dla każdego
odcinka okrągłego polecenie pobiera wymaganą grubość z `Insulation`
(EN ISO 12241, §4.12), zaokrągla ją w górę do najbliższej grubości
handlowej i podaje straty ciepła przy tej grubości. Jeśli kanał bez izolacji
już spełnia cel, polecenie podaje, że izolacja nie jest potrzebna, zamiast
najmniejszej grubości. Wynik trafia do znacznika odcinka (`insul_mm`,
`insul_required_mm`, `insul_material`, `insul_purpose`,
`insul_heat_loss_w_per_m`) oraz do etykiety na warstwie `WENTA-INSUL`, np.
`INS 20 mm mineral_wool (condensation)`. Ponowne uruchomienie aktualizuje
etykiety w miejscu. Odcinki prostokątne są pomijane z komunikatem, ponieważ
model biblioteki jest walcowy. Izolacja zwiększa wymiar zewnętrzny kanału,
co musi uwzględnić praca nad 3D i kolizjami z planu rozwoju.

### 3.15 `WENTASOUND` — hałas w pomieszczeniu dla każdego terminala

Należy zaznaczyć sieć. Zapytania: rodzaj pomieszczenia
`[studio/bedroom/office/classroom/retail/industrial]`, powierzchnia
przegród pomieszczenia (m²) i średni współczynnik pochłaniania α. Dla
każdego terminala polecenie bierze hałas regenerowany odcinka, który go
zasila, przenosi go do pomieszczenia równaniem pomieszczenia i porównuje
wynik z limitem dla rodzaju pomieszczenia (`DrawingSound.Rooms`, §4.10).
Pomieszczenia są wypisywane od najgorszego, wartości trafiają do znaczników
terminali (`sound_lp_db`, `sound_nc_limit`, `sound_ok`), a tabela do
**`%TEMP%\wenta_sound.csv`**. Tłumiki dla pomieszczeń powyżej limitu
dobiera `WENTAATTEN` (§3.18).

### 3.16 `WENTAHELP` — lista poleceń i podręcznik

Wypisuje wszystkie polecenia z jednowierszowym opisem oraz ścieżki
podręcznika angielskiego i polskiego, które `install.ps1` kopiuje obok DLL.
Zapytanie `Open the user guide [English/Polski/No] <No>` otwiera wybrany
podręcznik w domyślnej przeglądarce: stronę HTML ze spisem treści,
generowaną z podręcznika Markdown podczas budowania wtyczki
(`zwcad-plugin/tools/MakeHelp.cs`, `Wenta.Markdown`). Jeżeli pliku HTML
nie ma, otwierany jest plik Markdown programem, który Windows kojarzy
z `.md`.

### 3.17 `WENTAWRITE` / `WENTAREAD` — sieć JSON ⇄ rysunek

**`WENTAWRITE`** pyta o plik JSON sieci (domyślnie
`%TEMP%\wenta_read.json`; zmienne środowiskowe są rozwijane) i punkt
bazowy (domyślnie 0,0) i rysuje sieć w skali: każdy przewód to polilinia
na warstwie **`WENTA-DUCT`** o długości w milimetrach, każdy inny element
(źródło, trójnik, kształtka, przepustnica, tłumik, terminal) to okrąg
100 mm na tej samej warstwie, a etykieta trafia na **`WENTA-ANNO`**. Każdy
obiekt ma w znaczniku XData pełny rekord elementu: `cid`, `type`, GUID,
nazwę, `scope`, pozycję katalogową i pochodzenie, przekrój (z dokładnymi
metrami), parametry (długość, chropowatość, strumień, ζ …) oraz połączenia
od strony dopływu (`in.<port>` = `element:port`). Jeżeli JSON zawiera
`layout_mm` / `at_mm` dla każdego elementu (tak zapisuje go `WENTAREAD`),
używany jest ten układ; w przeciwnym razie sieć jest rozkładana od każdego
źródła: przewody w bieżącym kierunku, odgałęzienie każdego trójnika
obrócone o 90°. Ponowne `WENTAWRITE` aktualizuje istniejące obiekty
w miejscu (dopasowane po GUID, potem po zakresie i id) zamiast rysować
drugą kopię.

**`WENTAREAD`** odczytuje wszystkie oznaczone obiekty z obszaru modelu
z powrotem do sieci, sprawdza ją i zapisuje JSON w postaci projektowej,
którą przyjmuje też wycofany pythonowy `NetworkDesignSchema` (pytanie
o ścieżkę, domyślnie `%TEMP%\wenta_read.json`), po czym ją oblicza
i wypisuje ΔP. Przewód, którego narysowana długość różni się od znacznika
o więcej niż 0,5 mm, przyjmuje długość z rysunku, a jego znacznik
i etykieta są aktualizowane. Polecenie **odmawia** (nic nie zapisuje), gdy
w rysunku jest osierocony znacznik (element zasilany przez element, którego
już nie ma w rysunku), brakujący lub powielony GUID (skopiowany obiekt),
powielone id, klasa niezgodna z typem, rekord przewodu na obiekcie
punktowym lub odwrotnie, brakująca wartość, nieznany port albo sieć, która
nie przechodzi walidacji. Wypisywanych jest do 20 problemów.

### 3.18 `WENTAATTEN` — tłumiki dla pomieszczeń powyżej limitu

Odczytuje narysowaną sieć tak jak `WENTAREAD` (wszystkie oznaczone obiekty
w obszarze modelu) i odmawia, wypisując problemy, gdy rysunek nie
przechodzi walidacji. Zapytania: rodzaj pomieszczenia
`[studio/bedroom/office/classroom/retail/industrial]`, powierzchnia
przegród (m²), średni współczynnik pochłaniania α oraz katalog tłumików
(domyślnie `example-attenuators.json` obok DLL, §5.5). Dla każdego
terminala oblicza hałas w pomieszczeniu tak jak `WENTASOUND`, z odcinka
sztywnego, który zasila terminal (także przez kształtki dwuportowe),
odejmując tłumiki już zamontowane na tym odcinku. Dla każdego pomieszczenia
powyżej limitu wybiera spośród pozycji katalogu o średnicy odcinka
zasilającego (±0,5 mm) tę o najmniejszym ζ, której tłumienie wtrąceniowe
pokrywa przekroczenie (przy remisie krótszą), i wstawia ją przed terminalem
jako kształtkę dwuportową `atten_<terminal>` z ζ, GUID, pozycją katalogową
i pochodzeniem z katalogu. Sieć jest obliczana ponownie, a nowe elementy są
rysowane (500 mm wstecz wzdłuż ostatniego segmentu odcinka) przez
`WENTAWRITE`, więc `WENTAREAD` odczytuje je z powrotem. Pomieszczenie,
którego żadna pozycja katalogu nie sprowadza poniżej limitu, jest
zgłaszane, ale nie jest zmieniane. Tabela trafia do
**`%TEMP%\wenta_atten.csv`**, a zaktualizowana sieć do
**`%TEMP%\wenta_atten.json`**.

Dobór używa jednej wartości tłumienia wtrąceniowego na urządzenie wobec
jednoliczbowego poziomu w pomieszczeniu z §4.10; nie ma danych w pasmach
oktawowych, promieniowania przez ścianki ani propagacji wzdłuż przewodów,
więc wynik jest pierwszym sprawdzeniem. Urządzenia
w `example-attenuators.json` są fikcyjne.

### 3.19 `WENTAPROPS` / `WENTAMOVE` / `WENTASTRETCH` — edycja narysowanej sieci

Te trzy polecenia działają na sieci narysowanej przez `WENTAWRITE`
(§3.17). Każde odczytuje wszystkie rekordy elementów z obszaru modelu
tak jak `WENTAREAD` i odmawia, podając pierwszy problem, gdy rysunek nie
przechodzi walidacji. Każde pyta o identyfikator elementu (Enter, a
potem wskazanie obiektu, bierze identyfikator z jego znacznika),
wykonuje edycję w `Wenta.NetworkEdit`, oblicza sieć przed i po zmianie
i rysuje rekordy ponownie **w miejscu**: te same obiekty, GUID i XData,
z nowymi długościami, wymiarami i opisami, więc tożsamość każdego
elementu przetrwa edycję, a kolejne `WENTAREAD` nie znajduje zmian
długości. Komunikat podaje zmienione elementy i ΔP ścieżki krytycznej
przed i po zmianie.

- **`WENTAPROPS`** wyświetla właściwości elementu (średnicę albo
  szerokość i wysokość w mm, długość w m, przepływ w m³/s, ζ) oraz
  przepływ, który przez niego płynie, a następnie zmienia jedną z nich.
  Średnica jest zaokrąglana w górę do najbliższego wymiaru wg EN 1506
  (`StandardSizes.NearestRoundSize`, komunikat to zaznacza); szerokość i
  wysokość są przyjmowane bez zmian; przepływ ustawia się na terminalu i
  przenosi w górę sieci; długość oznacza rozciągnięcie (poniżej).
  Właściwość, której element nie ma (długość terminala, przepływ
  przewodu), oraz ujemne ζ są odrzucane.
- **`WENTASTRETCH`** nadaje przewodowi nową długość: ostatni wierzchołek
  przesuwa się wzdłuż ostatniego odcinka, a wszystkie elementy za nim
  przesuwają się o ten sam wektor, więc rysunek pozostaje połączony.
  Skrócenie, które usunęłoby ostatni odcinek, jest odrzucane.
- **`WENTAMOVE`** przesuwa element punktowy (trójnik, kształtkę,
  przepustnicę, tłumik, terminal, źródło) o przemieszczenie w mm. Końce
  dołączonych przewodów podążają za nim, a przewody te przyjmują nową
  narysowaną długość. Samych przewodów się nie przesuwa; należy je
  rozciągnąć albo przesunąć elementy na ich końcach.

### 3.20 `WENTAREFIT` — nowy przepływ terminala, dobór i równoważenie

Zapytania: terminal, jego nowy przepływ (m³/s) oraz metoda doboru
`[Velocity/EqualFriction]` z wartością docelową. W jednym poleceniu
ustawia przepływ, otwiera wszystkie przepustnice (kształtki
dwuportowe, których rekord ma rolę `damper`), dobiera każdy przewód do
jego nowego przepływu metodami `WENTASIZE` (`ReFit.Apply`), nadaje
kształtce w linii, która miała przekrój przewodu ją zasilającego, nowy
przekrój tego przewodu, oblicza sieć i ustawia przepustnicę na gałęzi
zasilającej każdy terminal na ζ, które wyrównuje ścieżkę terminala do
ścieżki krytycznej (`Balancing.BalancingZeta` przy prędkości w samej
przepustnicy; przepustnica na ścieżce krytycznej pozostaje otwarta).
Rekordy są rysowane ponownie w miejscu, więc nowe ζ trafia do znacznika
każdej przepustnicy. Wypisuje przewody o zmienionym wymiarze (stara i
nowa średnica oraz prędkość), ζ i otwarcie każdej przepustnicy oraz
terminale z nadwyżką ciśnienia bez przepustnicy (z ζ, którego
przepustnica by tam wymagała). Komunikat podaje ΔP przed i po, pozostałe
niezrównoważenie terminali z przepustnicą i czas obliczeń; sieć jest też
zapisywana do **`%TEMP%\wenta_refit.json`**.
`example-dampers-network.json` obok DLL to sieć referencyjna z
przepustnicą na każdej gałęzi; samą nazwę pliku, której `WENTAWRITE` nie
znajduje, wyszukuje się właśnie tam.

### 3.21 Karta wstęgi „Wenta”

Jeden panel z dwudziestoma jeden dużymi przyciskami, każdy uruchamia jedno polecenie:

| Przycisk | Polecenie |
|---|---|
| Duct Section | `WENTADUCT` |
| Wenta Panel | `WENTAPANEL` |
| Plugin Info | `WENTAHELLO` |
| Fitting Catalog | `WENTACATALOG` |
| BOM + KNR | `WENTABOM` |
| Tag Marker | `WENTATAG` |
| Trace Network | `WENTATRACE` |
| Pressure Drop | `WENTAPRESSURE` |
| Size Network | `WENTASIZE` |
| Balance Dampers | `WENTABALANCE` |
| Select Fan | `WENTAFAN` |
| Insulate | `WENTAINSULATE` |
| Room Sound | `WENTASOUND` |
| Attenuators | `WENTAATTEN` |
| Draw Network | `WENTAWRITE` |
| Read Network | `WENTAREAD` |
| Edit Properties | `WENTAPROPS` |
| Move Component | `WENTAMOVE` |
| Stretch Duct | `WENTASTRETCH` |
| Re-fit Flow | `WENTAREFIT` |
| Wenta Help | `WENTAHELP` |

### 3.22 Plan rozwoju

Polecenia takie jak `WENTAFITTING`
są planowane, nie zaimplementowane; część biblioteczna kilku z nich już istnieje (patrz §4).
Plan znajduje się w `zwcad-plugin/ROADMAP.md` oraz w zgłoszeniach GitHub
repozytorium `ModelTok/wenta`.

---

## 4. Podręcznik inżynierski (biblioteka)

Nazwy klas należą do przestrzeni nazw `Wenta`. Gdzie test w
`csharp/Wenta.Core.Tests/Program.cs` ustala wartość liczbową, jest ona
cytowana.

### 4.1 Powietrze (`Fluid`)

`Fluid.StandardAir()` — suche powietrze 20 °C, 101 325 Pa: ρ = 1,204 kg/m³,
μ = 1,825e-5 Pa·s. `Fluid.AirAtAltitude(altitudeM, temperatureC)` —
ciśnienie wg atmosfery wzorcowej ISA do tropopauzy, gęstość z równania gazu
doskonałego, lepkość wg Sutherlanda.

### 4.2 Geometria (`Round`, `Rectangular`, `Geometry`)

Przekroje mają `Area`, `HydraulicDiameter` (prostokątny: 2·W·H/(W+H)) i
`Perimeter`. `Geometry.EquivalentRoundDiameter(w, h)` to średnica
równoważna wg ASHRAE 1,30·(a·b)^0,625/(a+b)^0,25.

### 4.3 Tarcie i straty (`Friction`, `Losses`, `Flex`)

- `Friction.FrictionFactor(Re, ε/Dh)` — jawny współczynnik tarcia Darcy'ego
  wg Swamee–Jaina; przepływ laminarny 64/Re poniżej Re = 2300.
- `Friction.FrictionFactorColebrook(Re, ε/Dh)` — niejawne równanie
  Colebrooka–White'a rozwiązywane iteracją punktu stałego, start z
  Swamee–Jaina (tolerancja 1e-12, maks. 100 iteracji).
- `Losses.StraightPressureDrop(f, L, Dh, v, ρ)` — Darcy–Weisbach
  f·(L/Dh)·ρ·v²/2. `Losses.LocalPressureDrop(ζ, v, ρ)` — strata miejscowa
  ζ·ρ·v²/2.
- `Flex.StretchCorrectionFactor(D, rozciągnięcie%)` — korelacja z ASHRAE
  Fundamentals dla niedociągniętego przewodu elastycznego.

### 4.4 Wymiary znormalizowane (`StandardSizes`, `Standards`, `Settings`)

`StandardSizes` zawiera tablice EN 1505:2001 (prostokątne) i EN 1506:2007
(okrągłe) w mm oraz `NearestRoundSize`. `Standards` dodaje tablice
ASHRAE/SMACNA (szereg calowy w mm) i DIN 24155 (liczby normalne Renarda
R10/R20) za wyliczeniem `Standard` (`En1505_1506`, `AsHrae`, `Din`) z
`NearestRoundSizeFor`. `ProjectSettings` przechowuje domyślne ustawienia
projektu (norma EN, średnica domyślna 200 mm, chropowatość 0,0001 m, 4 m/s,
1 Pa/m, pomieszczenie akustyczne `office`, system jednostek `Si` lub `Ip`) z
metodą `Validate()`.

### 4.5 Dobór przekroju (`Sizing`) — pięć metod

Każda zwraca najmniejszy znormalizowany przekrój EN spełniający kryterium
(w ostateczności największy) jako `SizingResult { Section, Velocity,
PressureDropPerMeter }`:

| Metoda | Kryterium |
|---|---|
| `VelocityMethod(Q, shape, vTarget = 4)` | prędkość ≤ docelowa |
| `EqualFrictionMethod(Q, ΔpPerM = 1, shape, ε, fluid)` | jednostkowy spadek ciśnienia ≤ docelowy (metoda stałego spadku) |
| `PressureDropBudget(Q, L, budgetPa, …)` | całkowity spadek na długości L ≤ budżet (= stały spadek przy budżet/L) |
| `NoiseLimitMethod(Q, spaceType, …)` | prędkość ≤ `NoiseLimitsMs[spaceType]` (studio 2,5; sypialnia 3,0; biuro 4,0; klasa 4,5; handel 5,0; przemysł 7,5 m/s) |
| `AspectRatioMethod(Q, vTarget, aspectRatio = 2)` | przekroje prostokątne o stosunku boków ≥ zadany, najmniejsze pole spełniające prędkość |

### 4.6 Współczynniki strat miejscowych ζ kształtek

- `FittingsLibrary` — korelacje z ASHRAE Fundamentals, Hendigera i
  Idelczika: `ReducerRound` (redukcja), `ExpanderRound` (dyfuzor),
  `JunctionTeeBranch`, `JunctionTeeCombine` (trójnik rozdzielający /
  łączący), `DamperButterfly` (przepustnica, 0,1 w pełni otwarta),
  `DiffuserCeiling` (nawiewnik sufitowy, 0,4/areaThrow), `GrilleReturn`
  (kratka wywiewna, 0,25·(1+zasłonięcie)), `RectangularElbow` (kolano
  prostokątne wg Idelczika §6 z korektą proporcji), `MiteredElbow` (kolano
  segmentowe; kierownice zmniejszają stratę do 40 %).
- `ElbowRound(R, D, kąt)` — tablica kolan okrągłych Hendigera/Ziętka/
  Chludzińskiej (R/D 0,5–2,5; 20°–180°) interpolowana dwusześciennym splajnem
  not-a-knot.
- `ReCorrections` — łagodne mnożnikowe korekty katalogowego ζ względem liczby
  Reynoldsa i wymiaru, ograniczone do [0,75; 1,5] i [0,9; 1,3]; punkt
  odniesienia Re = 50 000, D = 200 mm.
- `ZetaCatalog` — otwarty katalog JSON (specyfikacja
  `csharp/catalogs/FORMAT.md`): `Load`/`Parse`, `ById`, `Match(type,
  sizeMm)` (pierwsze dopasowanie w kolejności pliku, domknięte okno
  wymiarowe w mm), `ZetaFor` (najpierw katalog, potem wbudowana korelacja
  dla `rect_elbow`, `mitered_elbow`, `damper`, `diffuser`, `grille`, `tee`;
  inne typy nie mają wartości zapasowej i zgłaszają wyjątek).
  `Merge(base, overrides)` nakłada katalogi wg identyfikatora pozycji,
  podmieniając w miejscu i dopisując do `Warnings` jeden wiersz na każde
  nadpisanie: `id X: zeta A (source S1) overridden by zeta B (source S2)`.
  Każda pozycja ma pochodzenie `source` i opcjonalną podpowiedź `knr`.

### 4.7 Sieć i solver (`Components`, `Network`, `Solver`)

Elementy: `Source` (centrala/wentylator, bez spadku), `RigidDuct` (kanał
sztywny, Darcy–Weisbach, chropowatość domyślna 0,1 mm), `FlexDuct` (przewód
elastyczny: Pa/m producenta × długość × współczynnik rozciągnięcia),
`TwoPortFitting` (kształtka przelotowa, ζ na wylocie), `Tee` (trójnik, porty
`combined`, `straight`, `branch`, osobne ζ na każdą odnogę), `Terminal`
(nawiewnik/wywiewnik: wymagany strumień, opcjonalny przekrój i ζ).
`Network.Add(id, element)`, `Network.Connect("a", "b.port")`, `Validate()`,
`Solve(fluid)`:

1. strumienie z elementów końcowych są propagowane w górę w odwrotnej
   kolejności topologicznej;
2. każdy element wylicza prędkość i spadek ciśnienia na swoich portach;
3. ścieżka krytyczna to najdłuższa (wg sumy spadków) droga od źródła do
   elementu końcowego (`Solver.CriticalPath`, `CriticalPathPressureDrop`).

Cykle zgłaszają `network graph contains a cycle`. Rozwiązanie jest
idempotentne. Wartości sprawdzone: łańcuch z README (centrala → kanał
okrągły D200, 20 m → nawiewnik 0,1 m³/s) daje **14,13 Pa**
(14,13473757973617); sieć wzorcowa z trójnikiem z §3.3 daje **7,63 Pa**.

### 4.8 Wyniki, analiza, oznaczanie

- `Results.ExtractResults(net)` — jeden `ComponentResult` na element
  (strumień i prędkość na wejściu/wyjściu, całkowity spadek);
  `ResultsAsCsv`, `ResultsSummary`.
- `Analysis.Analyze(net, fluid)` — rozwiązuje sieć i zwraca `CriticalDpPa`
  oraz `BranchInfo` dla każdego kanału (strumień, prędkość, spadek, hałas
  regenerowany, ζ regulacyjne). ζ regulacyjne przyjmuje własny spadek gałęzi
  jako przybliżenie ciśnienia dyspozycyjnego — jest to udokumentowane
  uproszczenie.
- `Marking.AssignBranchMarks(net)` — deterministyczne numery gałęzi (BFS)
  dla każdego `RigidDuct` z wymiarem (mm) i strumieniem; `MarksAsCsv`.

### 4.9 Regulacja (`Balancing`)

`RequiredZeta(Δp, v, ρ)` = 2·Δp/(ρ·v²); `BalancingZeta(totalReq,
branchAvail, v, ρ)` zwraca ζ przepustnicy dławiącej nadwyżkę (0, gdy gałąź
już spełnia wymaganie; test: 30 Pa wobec 10,736 Pa przy 4 m/s → ζ = 2,0);
`DamperOpenPercentage(ζ)` odwraca korelację przepustnicy motylkowej na
procent otwarcia.

### 4.10 Akustyka (`Sound`)

`RegeneratedNoiseRound(v, D, ρ)` — poziom mocy akustycznej hałasu
regenerowanego Lw = 10 + 10·log10(ρ/ρ0) + 60·log10(v) − 20·log10(D) dB re
1e-12 W (skalowanie typu Lighthilla v⁶; stała jest przesunięciem
kalibracyjnym, nie normą). `DuctPressureLevel(Lw, S, α)` — równanie
pomieszczenia dla pola rozproszonego Lp = Lw + 10·log10(4(1−α)/(α·S)).
`NcOk(spaceType, level)` / `NcOkTarget(nc, level)` porównują z
`NoiseLimitsNc` (studio 25, sypialnia 25, biuro 35, klasa 35, handel 40,
przemysł 60).

### 4.11 Wentylatory (`FanCurve`, `Fan`)

`FanCurve(name, FanPoint[])` — charakterystyka ciśnienia statycznego
producenta, strumienie ściśle rosnące, interpolacja odcinkowo-liniowa
`StaticPressureAt(Q)` (wyjątek poza zakresem tabeli). `Fan.Power(Q, p, η)` =
Q·p/η W (test: 1,5 m³/s, 800 Pa, η 0,4 → 3000 W). `Fan.Margin(curve, Q,
pReq)` (null poza zakresem) i `Fan.PickFan(curves, Q, pReq)` — pierwszy
wentylator z nieujemnym zapasem ciśnienia w punkcie pracy.

### 4.12 Izolacja (`Insulation`)

Stacjonarna sieć oporów cieplnych walca na metr (film wewnętrzny, izolacja
ln(Do/D)/(2πλ), film zewnętrzny), zgodnie z praktyką EN ISO 12241 / ASHRAE.
`RequiredThicknessCondensation(Tair, Tdew, Tamb, λ, D, hi, he)` zwiększa
grubość krokami 1 mm (maks. 250 mm), aż temperatura powierzchni pozostanie
powyżej punktu rosy (ochrona przed kondensacją);
`RequiredThicknessHeatLoss(…, targetWPerM, …)` — kryterium strat ciepła;
`HeatLossWithInsulation`; `SelectThickness(t)` zaokrągla w górę do
20/30/40/50/60/80/100/120 mm (0,045 m → 0,05 m). Tablica materiałów: wełna
mineralna 0,035, pianka PE 0,040, EPDM/NBR 0,038, PIR 0,024, pianka PU
0,028 W/(m·K). Domyślnie hi = 10, he = 8 W/(m²·K).

### 4.13 Pomieszczenia i krotność wymian (`Room`, `Units`)

`RoomBalance(nawiew, wywiew)` — strumień netto, `IsBalanced(tolerancja)`,
`ImbalanceFraction`. `RoomBalanceSet` sumuje wiele pomieszczeń i generuje
CSV `name,supply_m3s,exhaust_m3s,net_m3s,ach`. `Units.AirChangesPerHour(Q,
V)` = Q·3600/V (0,1 m³/s w 100 m³ → 3,6 h⁻¹).

### 4.14 Zestawienie materiałów, mapowanie KNR i eksport (`Bom`, `KnrMap`, `BomExport`)

`Bom.Build(net)` lub `Bom.Build(net, knrMap)` — jeden wiersz na element z
rodzajem (`duct`, `flex`, `fitting`, `terminal`, `source`), opisem,
długością, polem, strumieniem i kodem KNR. Bez mapy używane są kody
zastępcze z `Bom.KnrMap`. Z mapą `KnrMap` wczytaną z JSON (§5.2) najpierw
sprawdzane są nadpisania (dokładny `kind` + opcjonalny `shape`
`round|rectangular` + opcjonalny `type` `tee|inline|<klasa>`), potem
`codes[kind]`; pozycje niezmapowane otrzymują pusty kod i są wymienione raz
w `Bom.Unmapped` — kod nigdy nie jest wymyślany. Eksport: `Bom.ToCsv()`
(CSV ze średnikami), `BomExport.ToJson`/`SaveJson` (schema_version 1,
wiersze + sumy) oraz `BomExport.ToXlsx`/`SaveXlsx` (jeden arkusz, wiersz
nagłówka, wiersze, wiersz `TOTAL`; pakiet OOXML pisany ręcznie, bez
zależności zewnętrznych) — podstawa kosztorysu KNR.

### 4.15 Sieć w JSON (`NetworkJson`)

`Serialize(net, meta)` / `Save` oraz `Parse(json, out meta)` / `Load` —
wersjonowany JSON sieci (`schema_version` 1). Każdy element ma `id`,
`wenta_class`, trwały `guid` (generowany, gdy go brak), zarezerwowany
`drawing_scope`, `name` i argumenty konstruktora w notacji snake_case.
Połączenia to `"from": "id.port"`, `"to": "id.port"`. Nowsze wersje schematu,
nieznane klasy, zdublowane identyfikatory i brakujące pola wymagane są
odrzucane wyjątkiem `WentaException`. Zapis i odczyt zachowuje ścieżkę
krytyczną co do bitu (testy: 7,63 Pa i 14,13 Pa).

### 4.16 Topologia (`Topology`)

`Topology.Trace(polylines, TraceOptions)` scala polilinie osi kanałów 2D
(tolerancja przyciągania domyślnie 1e-4 m) w sieć drzewiastą: `Source`
(pierwszy koniec stopnia 1), łańcuchy `RigidDuct` (`duct0`, `duct1`, …),
`Tee` w wierzchołkach stopnia 3 i `Terminal` na pozostałych końcach
(`term0`, …). Średnice i strumienie końcówek pochodzą z
`TraceOptions.Diameters`/`Flows` (średnica domyślna 0,2 m). Wierzchołki
stopnia ≥ 4 i pętle są odrzucane. `TracedSystem.Flatten()` zwraca odcinki
`DuctSegment` do rysowania; `TotalLengthM()` sumuje długości łańcuchów.
Odnogi trójnika są przypisywane w kolejności przechodzenia, nie wg geometrii,
chyba że ustawiono `TraceOptions.StraightByDirection`: wtedy przelotem jest
odcinek, który najlepiej przedłuża kierunek odcinka dopływowego (ustawiają to
polecenia sieciowe, §3.6). `TraceOptions.SourceAt` wybiera jako źródło
otwarty koniec najbliższy danemu punktowi, a `TraceOptions.Sections`
nadpisuje przekrój odcinka.

### 4.17 Wykrywanie kolizji (`ClashDetection`)

`FindClashes(segments, clearanceM)` — dokładna najmniejsza odległość między
odcinkami osi 2D; para koliduje, gdy odległość < (Da + Db)/2 + prześwit.
Każda para zgłaszana raz, uporządkowana wg identyfikatora; `ClashesAsCsv`
zapisuje `a,b,distance_m`.

### 4.18 Prefabrykacja i rozwinięcia (`Fabrication`, `Development`)

`Fabrication.DuctSurfaceAreaM2(przekrój, L)` = obwód × L;
`DuctWeightKg(pole, grubośćMm, gęstość)` ze stalą 7850 kg/m³ domyślnie;
`FabricationBreakout` (długość odcinków prostych i kształtek);
`CuttingSchedule` (sumy na element, posortowane).
`Development.RoundDuctDevelopment`, `RoundElbowDevelopment`,
`ReducerConeDevelopment` zwracają oszacowania `FlatPiece` (szerokość,
długość, pole) — wyraźnie przybliżenia, bez naddatków na zamki i szwy.

### 4.19 Zestawienie danych elektrycznych (`Electrical`)

`ElectricalData(componentId, deviceType, powerW)` z opcjonalnym napięciem,
współczynnikiem mocy i częstotliwością; `Current()` = P/(U·cos φ).
`ElectricalSchedule` sumuje; `Electrical.AsCsv` generuje
`component_id,device_type,power_w,power_kw,voltage_v,current_a,power_factor,frequency_hz`.

---

## 5. Pliki danych do edycji przez użytkownika

Wszystkie cztery formaty to JSON w UTF-8 z całkowitą wersją schematu; czytnik
odrzuca plik deklarujący wersję nowszą niż rozumie, a nieznane klucze są
ignorowane. Liczby używają kropki dziesiętnej niezależnie od ustawień
regionalnych Windows.

### 5.1 Katalog ζ (`csharp/catalogs/FORMAT.md`, wersja 1)

Minimalny poprawny plik (ze specyfikacji):

```json
{
  "name": "my-office-defaults",
  "version": 1,
  "fittings": [
    { "id": "round-elbow-rd1.0", "type": "round_elbow", "zeta": 0.24,
      "source": "Hendiger tab. 4.3, R/D = 1.0" }
  ]
}
```

Pola pozycji: `id` (wymagane, unikatowe), `type` (znane wartości
`rect_elbow`, `round_elbow`, `mitered_elbow`, `tee`, `tee_straight`,
`reducer`, `damper`, `diffuser`, `grille`), `size_min_mm`/`size_max_mm`
(parami, `[w, h]` lub `[d]`), `zeta` (wymagane, liczbowe), `source`, `knr`,
`notes`. Wygrywa pierwsze dopasowanie; pozycja bez okna wymiarowego pasuje
do każdego wymiaru swojego typu. Dostarczone przykłady:
`example-generic.json` (instalowany z wtyczką), `example-generic-round.json`,
`example-vendor-style.json` (fikcyjny producent). Żadna z wartości
przykładowych nie jest zmierzonymi danymi producenta.

### 5.2 Mapowanie KNR (`csharp/catalogs/knr-example.json`, schema_version 1)

```json
{
  "schema_version": 1,
  "edition": "KNR 2-08 example (configure per your edition)",
  "codes": {
    "duct": "2-08 01xx-A (placeholder: round sheet-metal duct)",
    "flex": "2-08 02xx-A (placeholder: flexible duct)",
    "fitting": "2-08 03xx-A (placeholder: generic fitting)",
    "terminal": "2-08 04xx-A (placeholder: air terminal)",
    "source": ""
  },
  "overrides": [
    { "match": { "kind": "duct", "shape": "rectangular" },
      "code": "2-08 01xx-B (placeholder: rectangular sheet-metal duct)" },
    { "match": { "kind": "fitting", "type": "tee" },
      "code": "2-08 03xx-T (placeholder: tee / branch piece)" }
  ]
}
```

Każdy kod w dostarczonym pliku jest zastępczy — przed użyciem w kosztorysie
skonfiguruj go zgodnie ze swoim wydaniem KNR. Klucze zaczynające się od `_`
są ignorowane, więc plik może sam się dokumentować. Pusty ciąg oznacza
„celowo brak pozycji”; brak rodzaju pozostawia kod pusty i jest zgłaszany w
`Bom.Unmapped`. Wczytaj przez `KnrMap.Load(ścieżka)` i przekaż do
`Bom.Build`.

### 5.3 Sieć w JSON (`NetworkJson`, schema_version 1)

Sieć łańcuchowa z README zapisana przez `NetworkJson.Serialize` (GUID-y
skrócone):

```json
{
  "schema_version": 1,
  "name": "readme",
  "components": [
    { "id": "ahu",  "wenta_class": "Source",    "guid": "…", "drawing_scope": null, "name": "AHU" },
    { "id": "duct", "wenta_class": "RigidDuct", "guid": "…", "drawing_scope": null, "name": "duct",
      "cross_section": { "shape": "round", "diameter": 0.2 },
      "length": 20, "absolute_roughness": 0.0001 },
    { "id": "term", "wenta_class": "Terminal",  "guid": "…", "drawing_scope": null, "name": "terminal",
      "flowrate": 0.1, "zeta": 0 }
  ],
  "connections": [
    { "from": "ahu.outlet",  "to": "duct.inlet" },
    { "from": "duct.outlet", "to": "term.inlet" }
  ]
}
```

Klasy: `Source`, `RigidDuct` (`cross_section`, `length`,
`absolute_roughness`), `FlexDuct` (`diameter`, `length`,
`pressure_drop_per_meter`, `stretch_percentage`), `TwoPortFitting`
(`cross_section`, `zeta`), `Tee` (`cross_section`, `zeta_straight`,
`zeta_branch`), `Terminal` (`flowrate`, opcjonalny `cross_section`, `zeta`).
Przekroje prostokątne: `{ "shape": "rectangular", "width": w, "height": h }`.
Geometria w metrach, strumienie w m³/s. Opcjonalne argumenty konstruktora
można pominąć. Przy odczycie `type`/`source`/`target` są akceptowane jako
starsze aliasy `wenta_class`/`from`/`to`.

`NetworkJson.SerializeDesign` (używany przez `WENTAREAD`) zapisuje tę samą
sieć w postaci projektowej: `components` to obiekt kluczowany id, każdy
z `type` na początku, a połączenia mają postać `{ "source": "ahu:outlet",
"target": "duct:inlet" }`. `Parse` czyta obie postacie. Klucze opcjonalne,
zapisywane tylko gdy są ustawione: `catalog_entry`, `provenance`, `role`
(dla `TwoPortFitting`: `damper`, `attenuator`, …), `layout_mm` (oś
przewodu `[[x, y], …]` w milimetrach rysunku) i `at_mm` (`[x, y]` elementu
punktowego).

---

### 5.4 Katalog wentylatorów (`csharp/catalogs/example-fans.json`, wersja 1)

```json
{
  "name": "example-fans",
  "version": 1,
  "description": "...",
  "fans": [
    { "id": "ExampleFan EF-160", "efficiency": 0.55, "source": "fictional example",
      "points": [[0.0, 320.0], [0.1, 260.0], [0.2, 150.0], [0.3, 0.0]] }
  ]
}
```

Każdy wentylator ma `id` (niepowtarzalne w pliku) i `points`, listę par
`[flow_m3s, static_pa]` o ściśle rosnących strumieniach (co najmniej dwie).
`efficiency` (0 < η ≤ 1) jest opcjonalne i służy tylko do obliczenia mocy;
`source` i `notes` to dowolny tekst. Kolejność ma znaczenie: `WENTAFAN`
wybiera pierwszy wentylator, który spełnia punkt pracy, więc wentylatory
należy podawać od najmniejszego do największego. Wczytanie:
`FanCatalog.Load(path)`. Cztery wentylatory w `example-fans.json` są
zmyślone i służą tylko do sprawdzenia formatu.

### 5.5 Katalog tłumików (`csharp/catalogs/example-attenuators.json`, wersja 1)

```json
{
  "name": "example-attenuators",
  "version": 1,
  "description": "...",
  "attenuators": [
    { "id": "ExampleAtten EA-160-600", "diameter_mm": 160, "length_mm": 600,
      "insertion_loss_db": 8.0, "zeta": 0.45, "source": "fictional example" }
  ]
}
```

Każdy tłumik ma `id` (unikalne w pliku), średnicę przyłącza `diameter_mm`
i długość `length_mm` (obie dodatnie), jednoliczbowe tłumienie wtrąceniowe
`insertion_loss_db` (dodatnie) oraz `zeta` (≥ 0, przy prędkości
w przyłączu). `source` i `notes` to dowolny tekst; `source` trafia do
pochodzenia wstawionego elementu. Plik z `version` nowszą niż 1 jest
odrzucany. Wczytanie: `AttenuatorCatalog.Load(path)`. 21 urządzeń
w `example-attenuators.json` (Ø100–Ø315, 600/900/1200 mm) jest fikcyjnych
i służy tylko do sprawdzenia formatu.

## 6. Rozwiązywanie problemów

Fakty o platformie ZWCAD 2021 zapisane w `zwcad-plugin/README.md`:

- **Wtyczka się nie ładuje.** Rejestracja musi być w **HKLM**
  (`LOADCTRLS = 14`); wpisy w HKCU są ignorowane. Dlatego `install.ps1`
  wymaga podniesienia uprawnień (UAC). Sprawdź, czy `LOADER` wskazuje na
  istniejący plik `WentaZwcad.dll`.
- **Niewłaściwa platforma.** API zarządzane (`ZwManaged.dll`,
  `ZwDatabaseMgd.dll`) jest mieszane x64; DLL musi być zbudowana z
  `/platform:x64`, co robi `build.cmd`.
- **`NETLOAD` lub `MENULOAD` zawodzi w skrypcie.** Ustaw wcześniej `FILEDIA`
  na `0` (patrz `full_test.scr`), a potem z powrotem na `1`.
- **Brak karty na wstędze.** Wykonaj raz `MENULOAD` na `Wenta.CUIX`. Karta
  wymaga w CUIX atrybutów `DefaultDisplay="AddToWorkSpace"` i
  `WorkspaceBehavior="MergeOrAddTab"`; `uia-check.ps1` sprawdza jej obecność
  w drzewie interfejsu.
- **`WENTACATALOG` zgłasza brak przykładowego katalogu.** Polecenie szuka
  `example-generic.json` obok DLL; `install.ps1` kopiuje go do
  `C:\ProgramData\WentaZwcad`.
- **Co wtyczka faktycznie zrobiła?** Każde polecenie zapisuje wiersz do
  `%TEMP%\wenta_zwcad_test.txt`; `WENTABOM` zapisuje `%TEMP%\wenta_bom.csv`,
  `WENTATRACE` `wenta_trace.json`, `WENTAPRESSURE` `wenta_pressure.csv`,
  `WENTABALANCE` `wenta_balance.csv`, a `WENTASOUND` `wenta_sound.csv`,
  wszystkie w `%TEMP%`.
- **`open duct end(s) without a terminal flow`.** Każdy otwarty koniec poza
  źródłem wymaga znacznika terminala `WENTATAG` w promieniu 150 mm; komunikat
  podaje położenia.
- **Wyjątki.** W kodzie wtyczki `ZwSoft.ZwCAD.Runtime.Exception` przesłania
  `System.Exception`; błędy biblioteki to `Wenta.WentaException` z
  komunikatem wskazującym pole, plik lub element.
- **Znane ograniczenie.** `WENTABOM` rozwiązuje wbudowaną sieć wzorcową,
  a nie rysunek; narysowaną sieć odczytują `WENTATRACE` i pozostałe
  polecenia sieciowe (§3.6).

Zgłoszenia: <https://github.com/ModelTok/wenta/issues>. Dołącz wersję ZWCAD
wypisaną przez `WENTAHELLO`, odpowiednie wiersze z
`%TEMP%\wenta_zwcad_test.txt`, a przy problemach z biblioteką — komunikat
`WentaException`.
