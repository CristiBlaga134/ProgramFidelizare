# Note tehnice

Documentul explică cum este construit programul și, mai ales, **de ce** a fost
construit așa. Deciziile sunt mai utile decât descrierea codului, pe care oricum
îl puteți citi.

---

## 1. Constrângerea de pornire

Cerința a fost un program autonom, livrat ca un executabil pe care magazinul îl
deschide cu dublu clic. Aceasta a determinat aproape toate deciziile:

- **.NET 10 cu WPF**, publicat *self-contained* și *single-file* — platforma
  intră în executabil, deci nu se instalează nimic pe calculatorul din magazin;
- **SQLite** ca bază de date — un fișier, fără serviciu de pornit, fără
  configurare;
- **fără dependențe externe la rulare** — nici internet, nici server.

Rezultatul este un fișier de aproximativ 60 MB.

---

## 2. Structura pe straturi

```
Fidelizare.App  (WPF)
      ↓ depinde de
Fidelizare.Core  (reguli, entități, interfețe)
      ↑ implementate de
Fidelizare.Data  (SQLite)
```

`Fidelizare.Core` nu referă nici SQLite, nici WPF. Conține entitățile, calculele
și **interfețele** către stocare (`IUnitOfWork`, `ICustomerRepository` și
celelalte). `Fidelizare.Data` le implementează.

Motivele concrete, nu teoretice:

1. **Testabilitate.** `PointsCalculator` este un set de funcții pure. Regulile de
   rotunjire, pragurile de nivel și fereastra aniversară se verifică fără bază de
   date și fără ferestre.
2. **Timpul controlabil.** Expirarea punctelor depinde de „acum”. Interfața
   `IClock` permite ca un test să avanseze 13 luni instantaneu.
3. **Trecerea la mai multe case.** Dacă magazinul crește, se schimbă
   implementarea stratului de date. Restul aplicației nu se atinge.

---

## 3. Modelul de puncte

Aceasta este partea cu cele mai multe decizii neevidente.

### Loturi, nu un sold

Soldul unui client **nu** este un câmp în baza de date. Este suma punctelor
rămase din `PointLots` — pachete de puncte, fiecare cu propria dată de expirare.

Un câmp „sold” ar fi mai simplu, dar ar fi și greșit din trei motive:

- punctele expiră individual, la un an de la acordare — un singur număr nu poate
  spune *care* puncte expiră și *când*;
- un câmp întreținut separat se poate desincroniza de istoric; o sumă calculată
  nu poate;
- la consum trebuie folosite întâi punctele care expiră cel mai devreme, ca
  clientul să nu le piardă degeaba. Fără loturi, această ordonare nu există.

### Registrul este append-only

`PointTransactions` nu se modifică și nu se șterge niciodată. Orice corecție este
o mișcare nouă, cu semn opus. Fiecare linie păstrează și soldul rezultat.

Consecința practică: la întrebarea „de ce am atâtea puncte?” se poate reconstitui
întregul drum. Într-un program care ține evidența unor beneficii cu valoare
reală, aceasta nu este o rafinare, ci o cerință.

### Evidența consumului

`PointLotConsumptions` reține din ce lot au fost luate punctele fiecărei
operațiuni de consum.

Fără această legătură, anularea unei recompense ar fi aproximativă: punctele
s-ar restitui cu o valabilitate nouă, adică clientul ar câștiga timp pe care nu
îl avea. Cu ea, restituirea se face exact în loturile originale, cu datele lor
de expirare. Punctele ale căror loturi au expirat între timp nu se restituie, iar
operatorul este informat.

### Anularea unei cumpărări

Punctele acordate se retrag întâi din lotul generat de acea cumpărare, apoi, dacă
au fost deja cheltuite, din celelalte loturi deschise, în ordinea expirării.

Dacă nici așa nu ajung, **soldul coboară la zero, dar nu devine negativ**, iar
operațiunea raportează cât nu s-a putut retrage. Un sold negativ ar bloca
clientul la casă pentru o greșeală a magazinului, ceea ce ar fi mai rău decât
pierderea contabilă.

### Returul parțial

Programul nu cunoaște produsele de pe bon, doar totalul, deci returul se
consemnează ca sumă. Fiecare retur are propria înregistrare (`PurchaseReturns`),
iar pe bon se țin totalurile: suma returnată și punctele retrase.

Punctele de retras se calculează proporțional, pornind de la **punctele efectiv
acordate**, nu de la regulile de acum: clientul păstrează
`floor(puncte acordate × rest / total)`, iar se retrage diferența față de ce fusese
deja retras. Așa multiplicatorul de nivel și campania din ziua vânzării rămân
valabile chiar dacă regulile s-au schimbat între timp, iar retururile succesive pe
același bon ajung împreună exact la punctele acordate. Funcția este
`PointsCalculator.PointsToReverseForReturn`.

Retragerea folosește aceeași cale ca anularea, inclusiv regula soldului care nu
devine negativ. Rulajul pentru nivel, totalul de la înscriere și rapoartele de
vânzări scad suma returnată. Când tot bonul a fost returnat, bonul se închide ca
anulat, ca să nu mai conteze nici la numărul de cumpărături. O anulare după un
retur parțial retrage doar punctele rămase.

Rapoartele atribuie returul zilei vânzării, nu zilei returului: vânzările unei
zile trecute pot scădea după un retur făcut mai târziu.

### Rotunjirea

Se face în jos, la fiecare pas. Un bon de 97 lei la 10 lei/punct dă 9 puncte, iar
la nivelul Silver (×1,25) dă 11, nu 12. Clientul nu primește niciodată mai mult
decât a cheltuit, iar regula se poate explica la casă într-o propoziție.

---

## 4. Deciziile din stratul de date

### Banii se păstrează ca numere întregi

Toate sumele sunt stocate în **bani**, ca `INTEGER`. SQLite nu are tip zecimal.
Dacă sumele ar fi `REAL`, un `SUM()` peste câteva mii de bonuri ar aduna erori de
rotunjire vizibile în raportul lunar. Cu numere întregi, adunarea este exactă
indiferent câte rânduri sunt.

Multiplicatorii și procentele se păstrează în miimi, din același motiv.

Un test verifică explicit acest lucru: 0,10 lei adunat de o mie de ori dă exact
100 lei.

### Unitatea de lucru și tranzacțiile

Acordarea de puncte atinge simultan `Purchases`, `PointLots` și
`PointTransactions`. `IUnitOfWork` cuprinde toate operațiile într-o tranzacție.
Dacă nu se apelează `Commit()`, tranzacția se anulează la închidere — deci
ieșirea pe o excepție lasă baza de date coerentă, fără cod special.

Tranzacțiile sunt **imediate**, nu amânate: blocajul de scriere se ia din start.
Altfel, două case care citesc și apoi scriu simultan ar putea primi eroare de
blocare la jumătatea unei operațiuni deja construite pe jumătate.

Consecința acestei alegeri este că **două unități de lucru nu pot fi deschise în
paralel pe același fir de execuție** — a doua ar aștepta după prima, care așteaptă
după apelul în curs. Fiindcă blocajul s-ar manifesta ca o înghețare de 15 secunde
urmată de o eroare obscură, `SqliteUnitOfWorkFactory` numără unitățile deschise
și aruncă imediat o excepție explicită. Greșeala a apărut efectiv în timpul
dezvoltării, într-un test; garda a fost adăugată ca urmare.

### Jurnalizarea se adaptează la locul bazei de date

Local se folosește WAL, care permite citiri în paralel cu scrierea. WAL se
bazează însă pe maparea fișierului în memorie, care nu funcționează corect peste
SMB. Când calea este pe rețea, programul detectează singur situația și trece pe
jurnalul clasic, cu `synchronous = FULL`.

**Limitare de reținut:** varianta pe rețea funcționează pentru două-trei case
dintr-un magazin. Nu este o soluție pentru un lanț de magazine — acolo ar fi
nevoie de un server de baze de date propriu-zis, ceea ce stratul de date permite
fără a rescrie aplicația.

### Căutarea fără diacritice

`Customers` are o coloană `SearchKey` cu numele, telefonul, e-mailul și codul de
card reunite, cu litere mici și fără diacritice.

La casă nimeni nu tastează diacritice. Fără această coloană, o căutare după
`taranu` nu ar găsi `Țăranu`, iar clientul ar fi trimis să își caute cardul —
ceea ce anulează rostul căutării rapide.

### Migrările

Versiunea schemei se ține în `PRAGMA user_version`. La pornire se compară cu
versiunea așteptată și se aplică doar pașii lipsă. O versiune nouă a aplicației
se copiază peste cea veche fără ca magazinul să piardă date și fără operațiuni
manuale.

Dacă fișierul are o schemă *mai nouă* decât programul, pornirea se oprește cu un
mesaj clar, în loc să scrie în structuri pe care nu le înțelege.

---

## 4b. Configurabilitatea

Cerința a fost ca magazinul să nu depindă de o versiune nouă a programului pentru
a-și schimba regulile. Consecințele în cod:

**Nivelurile au fost dintotdeauna date, nu cod.** Bronze, Silver și Gold sunt
rânduri inserate la prima pornire, nu constante. Testele din
`TierCustomizationTests` pornesc de la scheme complet diferite — cinci niveluri,
două, unul singur — și verifică faptul că întregul program le urmează.

**Pragul de nivel nu mai este o sumă.** Câmpul se numește `Threshold`, nu
`MinQualifyingSpend`, iar în baza de date `ThresholdScaled`: după ce baza de
calcul a devenit configurabilă, un nume care spune „cheltuit” ar fi fost activ
înșelător într-un program bazat pe puncte. Scalarea cu o sută este uniformă,
deci aceeași coloană ține și lei, și puncte, și număr de vizite.

**Punctele pentru nivel se numără din registru, nu din sold.** Se însumează doar
mișcările care aduc puncte, plus retragerile pentru vânzări anulate. Consumul pe
recompense rămâne în afară — altfel clientul ar coborî din nivel tocmai fiindcă
și-a folosit punctele, adică ar fi pedepsit pentru că a participat la program.

**Campaniile se combină prin maxim, nu prin produs.** Două campanii ×2 suprapuse
din neatenție ar fi însemnat ×4. Regula „se aplică cea mai generoasă” este ușor
de explicat și imposibil de transformat în accident.

**Câmpurile proprii sunt definiții în baza de date**, cu valorile într-un tabel
separat. Controalele din formular se construiesc la deschiderea ferestrei, pentru
că nu se știe dinainte nici câte sunt, nici ce tip au. Valorile primesc și o
formă normalizată, ca să intre în căutarea fără diacritice în aceleași condiții
ca numele.

**Culorile de accent se referă prin `DynamicResource`.** Prima variantă folosea
`StaticResource` și schimba culoarea direct în obiectul pensulă. Nu a funcționat:
o referință statică se rezolvă o singură dată, la încărcarea ferestrei, iar
schimbarea nu se vedea decât după repornire. Cu referințe dinamice, înlocuirea
resursei se propagă imediat în toate ecranele deschise.

**Formatul codului de card** este prefix plus un număr de cifre. Numerotarea
automată ignoră codurile care nu sunt formate exclusiv din cifre, deci un cod
importat de forma `VIP-12` nu strică seria.

## 4ante. Un singur nume pe cont

Conturile aveau două nume: unul de autentificare, fără spații, și unul afișat în
rapoarte. Separarea este obișnuită în sistemele mari și complet nejustificată aici.

Argumentul care a decis: **autentificarea se face o dată pe tură.** Scurtimea unui
identificator de tip „ana" economisește câteva apăsări de tastă pe zi. În schimb,
rapoartele și jurnalul de audit se citesc des, iar acolo „ana" și „ana2" nu spun
nimic. Costul separării era permanent — două câmpuri obligatorii la fiecare cont
și întrebarea „care nume îl scriu la intrare” la fiecare angajat nou.

A rămas un singur nume, care se și tastează, și se citește. Are voie să conțină
spații și diacritice, trebuie să fie unic și de cel puțin trei caractere.

Consecința acceptată: doi angajați cu nume identic nu pot coexista, iar al doilea
trebuie diferențiat. Este rar și, la scara unui magazin, mai ieftin decât alternativa.

**Migrarea** (schema versiunea 3) mută numele afișat peste cel de autentificare,
fiindcă el este cel scris pentru oameni. Numele afișate nu erau însă unice, deci
la conflict câștigă primul, iar ceilalți își păstrează numele vechi de
autentificare; dacă nici acela nu e liber, se numerotează. Nimeni nu rămâne fără
un nume valid, deci nimeni nu rămâne pe dinafară după actualizare.

Cinci teste verifică migrarea pe o bază în forma veche: preluarea numelui afișat,
conturile fără nume afișat, coliziunile, dispariția coloanei și faptul că a doua
deschidere nu mai schimbă nimic.

## 4bis. Reducerea erorilor la casă

Două decizii luate după ce am urmărit fluxul real al unei revendicări.

**Reducerea se completează singură.** Prima variantă doar afișa suma, iar casierul
o retasta în câmpul bonului. Era locul cel mai probabil de greșeală din tot
programul: o cifră lipsă însemna client încasat integral deși tocmai își plătise
cu punctele, iar diferența s-ar fi văzut abia la casa de marcat, dacă cineva se
uita. Acum suma se adaugă automat peste reducerea comercială deja trecută, iar o
bandă reamintește că trebuie aplicată **și** pe casa de marcat — legătura cu
aparatul fiscal rămâne manuală și nu poate fi ascunsă.

**Casierul își poate anula propria recompensă, într-un interval scurt.** Regula
completă: responsabilul poate oricând; casierul doar recompensa acordată de el,
doar în intervalul configurat (implicit 10 minute) și doar înainte de a
înregistra vânzarea.

Ultima condiție este cea care contează: după înregistrare, bonul poartă deja
reducerea, iar restituirea punctelor ar lăsa o vânzare cu o reducere fără
acoperire. De acolo încolo corectura trece prin anularea vânzării, care retrage
și punctele acordate.

Motivul anulării este scris automat de program. La casă, cu clientul în față, o
casetă în care trebuie tastată o explicație ar face corectura mai costisitoare
decât greșeala — iar cine și când a anulat rămâne oricum în jurnalul de audit.

## 4c. Schimbul de date

Importul și exportul trec prin CSV cu **punct și virgulă**, nu virgulă: în
setările regionale românești virgula este separator zecimal, iar un fișier cu
virgule ar deschide „1,50 lei” ca două coloane. Fișierele se scriu cu marcaj de
ordine a octeților, altfel Excel presupune codificarea locală și strică
diacriticele. La citire, separatorul este ghicit din linia de antet — fișierele
vin de la programe diferite, iar întrebarea „ce separator are?” este una la care
utilizatorul de multe ori nu știe răspunsul.

Importul are **două faze**: o trecere care raportează ce s-ar întâmpla, fără să
scrie nimic, și execuția propriu-zisă. Pe o listă de câteva sute de clienți, o
coloană greșit denumită se observă mult mai greu după decât înainte.

Fiecare client se înscrie în tranzacția lui, nu într-una singură pentru tot
fișierul: o linie greșită nu anulează munca celorlalte, iar raportul final spune
exact ce a intrat și ce nu.

## 5. Securitate

**Parolele** se păstrează ca amprente PBKDF2-HMAC-SHA256 cu 210.000 de iterații și
sare aleatorie per cont. Numărul de iterații se salvează odată cu amprenta, ca să
poată fi mărit în viitor fără invalidarea conturilor existente. Comparația se
face în timp constant.

**Nu există cont implicit.** Un cont `admin/admin` livrat în executabil ar fi
identic la toate magazinele. Primul administrator se creează la prima pornire.

**Blocarea la autentificare** se face după 5 încercări eșuate, timp de 5 minute,
și se ține în memorie, nu în baza de date: scopul este să încetinească o
încercare de ghicire în sesiunea curentă, nu să lase un casier blocat după o
repornire.

**Jurnalul de audit** reține corecțiile de puncte, anulările, modificările de
reguli și operațiunile pe conturi, cu autorul și valorile de dinainte și de după.

**Ce nu face programul:** nu criptează fișierul bazei de date. Cine are acces
fizic la calculator poate citi datele clienților. Pentru un magazin cu un singur
punct de lucru, protecția potrivită este cea a sistemului de operare —
BitLocker și un cont Windows cu parolă. Menționez limitarea pentru că este o
decizie conștientă, nu o omisiune.

---

## 6. Interfața

MVVM scris de mână, fără bibliotecă externă: `ViewModelBase` și `RelayCommand`
înseamnă vreo 60 de linii, iar o dependență în plus ar fi crescut executabilul
fără să rezolve ceva.

Ecranele complexe (casa) au model de vizualizare separat. Formularele simple au
logica în code-behind — `PasswordBox` oricum nu poate fi legat prin binding, din
motive de securitate.

Ecranele se construiesc la prima folosire și rămân în memorie, ca revenirea la un
ecran să regăsească ce era acolo.

**Detaliu care a costat timp:** WPF închide aplicația când se închide ultima
fereastră. Fiindcă la pornire ferestrele de configurare și de autentificare sunt,
pe rând, singurele deschise, autentificarea reușită declanșa închiderea
programului chiar în momentul în care ar fi trebuit să înceapă. Aplicația
pornește acum cu `ShutdownMode = OnExplicitShutdown` și trece la
`OnMainWindowClose` abia după afișarea ferestrei principale.

**Culoarea textului se moștenește, nu se impune.** Stilul implicit de `TextBlock`
din temă stabilește fontul și dimensiunea, dar **nu** culoarea. Motivul este o
capcană a WPF care costă timp până e găsită: un stil implicit de `TextBlock` se
aplică și blocurilor de text pe care WPF le creează singur pentru conținutul
butoanelor și al altor controale. Culoarea din stil învinge culoarea moștenită de
la control, așa că butoanele barei laterale — cărora le fusese cerut text deschis —
scriau cu culoarea închisă implicită pe fundal bleumarin, adică erau invizibile.

Culoarea implicită este acum stabilită o singură dată, pe `Window`, de unde se
moștenește normal în tot cuprinsul ferestrei. Orice control care își schimbă
culoarea textului o transmite corect conținutului său. Regula de urmat la
adăugarea de stiluri noi: pe suprafețe întunecate se setează culoarea pe *control*,
niciodată printr-un stil implicit de `TextBlock`.

**Dimensionarea ferestrelor** nu se bazează pe înălțimi fixe scrise în XAML. O
valoare care pare rezonabilă pe monitorul dezvoltatorului nu spune nimic despre
calculatorul din magazin: Windows folosește frecvent scalare de 125% sau 150%,
iar la 125% pe un ecran de 1536×864 rămân echivalentul a 652 de unități logice
utile — mai puțin decât cereau majoritatea formularelor.

Soluția are trei părți, toate necesare:

- `SizeToContent="Height"` — fereastra crește exact cât are nevoie conținutul,
  indiferent de scalare sau de dimensiunea fonturilor de sistem;
- `WindowSizing.FitToWorkArea` — leagă `MaxHeight` și `MaxWidth` de zona de lucru
  a ecranului și coboară un `MinHeight` prea mare, ca fereastra să nu ceară
  niciodată mai mult decât există. Se reaplică la `SourceInitialized`, pentru
  cazul în care se conectează un monitor cu altă rezoluție;
- un `ScrollViewer` în interior — când limita de mai sus intră în acțiune,
  conținutul rămâne accesibil prin derulare, în loc să fie tăiat.

Toate ferestrele sunt și redimensionabile. Formularele lungi au fost în plus
rearanjate pe două coloane, ca perechile firești — nume și utilizator, parolă și
confirmare — să stea alături, ceea ce scurtează formularul cu aproximativ o
treime și elimină derularea în situația obișnuită.

**Cultura** este fixată pe `ro-RO`, inclusiv `FrameworkElement.LanguageProperty`
— altfel controalele WPF ar formata datele în engleză indiferent de setările
Windows. Din același motiv, publicarea **nu** folosește
`InvariantGlobalization`, deși ar fi redus dimensiunea fișierului.

Câmpurile de sume acceptă atât virgula, cât și punctul. Pe tastatura numerică
punctul este mai la îndemână, iar dacă `199.90` ar fi citit ca 19990 lei,
greșeala s-ar vedea abia în raportul de la sfârșitul zilei.

---

## 7. Testarea

104 teste automate, concentrate acolo unde greșelile ar costa bani:

- rotunjirea punctelor și aplicarea multiplicatorului de nivel;
- încadrarea pe niveluri, la limitele pragurilor;
- expirarea și **ordinea de consum a loturilor**;
- anularea cumpărărilor, inclusiv cazul în care punctele au fost deja cheltuite;
- anularea recompenselor și restituirea în loturile originale;
- retururile parțiale: calculul proporțional, retururile succesive pe același bon,
  efectul asupra nivelului și anularea după un retur;
- fereastra aniversară, inclusiv trecerea peste 31 decembrie și 29 februarie în
  anii nebisecți;
- căutarea cu diacritice;
- exactitatea sumelor după o mie de operațiuni.

Fiecare test primește o bază de date proprie, într-un fișier temporar, și un ceas
controlabil.

```powershell
dotnet test
```

Interfața a fost verificată separat, prin automatizare UI: pornire, autentificare,
navigare pe toate ecranele, căutarea unui client și înregistrarea unei vânzări cu
verificarea punctelor rezultate.

---

## 8. Lucruri conștient lăsate în afară

Le enumăr ca să fie clar că lipsesc din alegere, nu din scăpare:

- **Fără tipărire de bonuri sau carduri.** Programul calculează reducerea și o
  afișează; aplicarea pe bonul fiscal rămâne manuală, la casa de marcat.
- **Fără integrare cu casa de marcat.** Ar depinde de modelul de casă din fiecare
  magazin.
- **Fără SMS sau e-mail către clienți.** Ar necesita servicii externe,
  abonamente și conformare la reguli de comunicare comercială — adică exact
  contrariul unui program autonom.
- **Fără permisiuni configurabile pe rol.** Cele trei roluri sunt fixe. Pentru un
  magazin cu câțiva angajați, o matrice de permisiuni ar fi mai ușor de configurat
  greșit decât de folosit.
- **Fără sincronizare între magazine.** Vezi limitarea de la secțiunea 4.
