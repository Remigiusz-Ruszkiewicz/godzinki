# Godzinki — Blazor WebAssembly
Pierwsza wersja aplikacji osobistej na iPhone, macOS i Windows. Interfejs po polsku,
kwoty netto PLN, Supabase Auth i PostgreSQL z izolacją danych użytkownika.

## Uruchomienie
Wymagany .NET SDK 10.
```sh
dotnet restore
dotnet run --no-launch-profile --urls http://localhost:5260
```
Otwórz http://localhost:5260. Demo działa bez konta i nie zapisuje do Supabase.
Do danych rzeczywistych utwórz konto aplikacji i potwierdź e-mail.
Konto panelu administracyjnego Supabase nie jest kontem aplikacji.

## Zakres tej wersji
- Logowanie, rejestracja z potwierdzeniem e-maila, odświeżanie sesji, wylogowanie.
- Pulpit i historia zatwierdzonych kwot, kalendarz, osobna praca dodatkowa.
- Statystyki od początku historii, filtry lat i własnego okresu, wykresy miesięczne
  i roczne: zarobki, narastająco, ponad podstawę oraz czas do wypłaty. Rekord
  miesiąca, średnia, progi sumy zarobków i porównanie wspólnych miesięcy rok do roku.
- Bezpośredni wybór miesiąca i roku; historia rozliczeń pogrupowana rocznikami.
- Czas przez północ, przerwy, ujemne saldo i ręczne nadpisanie salda.
- Historyczne stawki netto; ręcznie zatwierdzane miesięczne migawki rozliczeń.
- Premie i korekty, rzeczywiste wypłaty, księga urlopów, cele.
- Eksport JSON i miesięczny CSV; import przygotowanego prywatnego pakietu historii.
- Zapis z kontrolą wersji; konflikt nie nadpisuje bez ostrzeżenia drugiego urządzenia.

## Granice wersji
Zapis i odczyt danych wymagają internetu. PWA po publikacji buforuje pliki aplikacji,
ale nie implementuje kolejki zmian offline. Zmiana danych na drugim urządzeniu
jest widoczna po odświeżeniu. Brak stoperów, powiadomień, bezpośredniego odczytu XLSX oraz
przywracania JSON. Zatwierdzone rozliczenia nie zmieniają się po edycji dni.
Urlop w kalendarzu i księga puli urlopowej są oddzielne; formularz to wyjaśnia.
Pełna podstawa w szacunku miesiąca nie oznacza kwoty zarobionej do dzisiaj.
Cel earned agreguje rozliczenia według pierwszego dnia miesiąca; paid według dat wpłat.

## Migracja historii
W Ustawieniach zalogowany użytkownik może wybrać prywatny plik w formacie
`godzinki-migration-v1`. Pakiet musi być przygotowany dla adresu konta.
Import wysyła partie po 100 wpisów do Supabase, zapisuje archiwum źródła i nie
nadpisuje istniejących rekordów. Po przerwaniu można ponowić ten sam pakiet.
Zapis nie jest transakcją obejmującą cały plik; wcześniejsze partie pozostają zapisane.
Konflikt daty z innym wpisem zatrzymuje import i wymaga rozstrzygnięcia.
Rozliczenia `legacy_excel` zachowują ręczny czas i kwoty; nie sumują ponownie E i G.
Prywatne pakiety, oryginalne arkusze i hasła muszą pozostawać poza repozytorium
i poza katalogiem publikowanym na Pages. Eksport aplikacji nie zawiera surowego
archiwum importu, dlatego należy zachować oryginał i pakiet migracji.

## Konfiguracja Supabase
Adres i klucz publishable są w wwwroot/appsettings.json, zgodnie z przeznaczeniem
publicznego klienta. Nigdy nie dodawać service_role ani secret do tej aplikacji.
Schemat bazy i testy RLS znajdują się w katalogu database.
Każda tabela wymaga RLS i polityki właściciela.
Authentication → URL Configuration: dodać http://localhost:5260/ do Redirect URLs.
Po wdrożeniu ustawić Site URL na adres GitHub Pages oraz dodać go do Redirect URLs.
Nie ustawiać szerokich wildcardów dla domen współdzielonych.

## GitHub Pages
Przygotowany workflow .github/workflows/pages.yml buduje i publikuje aplikację.
1. Umieść zawartość tego folderu w repozytorium GitHub (bez bin/ i obj/).
2. Settings → Pages → Source: GitHub Actions.
3. Uruchom workflow Deploy Godzinki albo wypchnij commit na main.
4. W Supabase dodaj dokładny URL strony do konfiguracji adresów powrotu.
Workflow ustawia base href automatycznie dla nazwy repozytorium.
Ta wersja używa nawigacji wewnętrznej bez podstron URL, więc odświeżanie strony
na GitHub Pages nie wymaga przekierowania SPA.

## Testy
```sh
dotnet publish -c Release -o publish
```
Testy finansowe obejmują zaokrąglenia, brak stawki, ręczne korekty, ujemne saldo,
zmianę nocną i przerwy. Testy RLS wykonano osobno na bazie.

