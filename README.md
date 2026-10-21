# Program de fidelizare

Aplicație desktop pentru Windows care gestionează un program de fidelizare într-un
magazin de haine: clienții acumulează puncte la cumpărături, urcă în niveluri și
schimbă punctele pe reduceri.

Se livrează ca **un singur fișier executabil**. Nu are nevoie de internet, de
server, de baze de date instalate separat sau de vreo procedură de instalare —
se copiază pe calculatorul din magazin și se deschide cu dublu clic.

---

## Cum funcționează programul

**Acumulare.** La fiecare bon, clientul primește un punct pentru fiecare 10 lei
încasați (valoare configurabilă). Rotunjirea se face în jos la fiecare pas: un
bon de 97 lei aduce 9 puncte, nu 10.

**Niveluri.** Clientul urcă automat în funcție de activitatea din ultimele 12
luni. **Nivelurile nu sunt fixate în program:** magazinul își definește câte
vrea, cu ce denumiri, praguri și multiplicatori vrea. Bronze, Silver și Gold sunt
doar ce găsește la prima pornire, ca să nu plece de la un ecran gol.

Baza de calcul este și ea la alegere: lei cheltuiți, puncte acumulate sau număr
de cumpărături.

**Recompense.** Punctele se schimbă pe reduceri fixe, reduceri procentuale sau
produse gratuite, dintr-un catalog editabil. O recompensă poate cere un bon
minim sau un anumit nivel.

**Expirare.** Punctele expiră după 12 luni de la acordare. Fiecare pachet de
puncte are propria dată de expirare, iar la revendicare se consumă întâi cele
care expiră cel mai devreme, ca să nu se piardă degeaba. La casă apare un
avertisment când clientul are puncte aproape de expirare.

**Campanii.** Perioade cu acumulare accelerată: puncte duble într-un weekend,
×1,5 toată luna decembrie, puncte triple în fiecare zi de marți. Se aplică peste
multiplicatorul de nivel. Dacă mai multe se suprapun, se ia cea mai generoasă —
nu produsul lor.

**Bonusuri.** 50 de puncte la înscriere și 100 de puncte o dată pe an, în
intervalul de 7 zile din jurul zilei de naștere.

Toate valorile de mai sus se modifică din aplicație, fără recompilare.

---

## Ce își configurează magazinul singur

Programul este gândit ca o platformă, nu ca o aplicație cu reguli fixe. Din
**Administrare**, fără nicio modificare de cod:

| Ce | Unde |
|---|---|
| Nivelurile: câte, cum se numesc, praguri, multiplicatori, culori | Niveluri |
| Baza de calcul a nivelului: lei, puncte sau număr de cumpărături | Reguli |
| Catalogul de recompense, cu praguri și rezervări pe nivel | Recompense |
| Campanii promoționale, pe perioade și pe zile ale săptămânii | Campanii |
| Câmpuri proprii pe fișa clientului: mărime, oraș, stil preferat | Câmpuri client |
| Logo, culoarea programului, formatul codului de card | Identitate |
| Toate regulile de acumulare, expirare și bonusuri | Reguli |
| Conturile personalului, pe trei roluri | Utilizatori |

**Import și export.** Clienții existenți se aduc dintr-un fișier CSV, cu
verificare înainte de scriere. Clienții, cumpărăturile și istoricul punctelor se
exportă în CSV, gata de deschis în Excel.

---

## Documentație

- **[Manual de utilizare](docs/manual-utilizare.md)** — pentru personalul din
  magazin: cum se lucrează la casă, cum se înscriu clienții, ce înseamnă fiecare
  ecran.
- **[Note tehnice](docs/note-tehnice.md)** — pentru cine întreține programul:
  structura codului, schema bazei de date, deciziile de proiectare și motivele
  lor.

---

## Structura soluției

```
ProgramFidelizare.sln
├── src/
│   ├── Fidelizare.Core     Regulile programului. Fără dependențe externe.
│   ├── Fidelizare.Data     Stocarea în SQLite: schemă, migrări, repository-uri.
│   └── Fidelizare.App      Interfața WPF.
├── tests/
│   └── Fidelizare.Tests    90 de teste: motorul de puncte, configurabilitatea, importul.
└── docs/
```

Separarea nu este decorativă. `Fidelizare.Core` nu știe nimic despre SQLite și
nimic despre ferestre: conține doar entitățile, calculele și interfețele către
stocare. Din acest motiv regulile de business pot fi testate direct, fără
interfață, iar trecerea de la un singur calculator la mai multe case de marcat
înseamnă schimbarea unui singur strat.

---

## Cerințe pentru dezvoltare

- .NET SDK 10.0 sau mai nou
- Windows (interfața folosește WPF)

## Compilare și rulare

```powershell
dotnet build                                        # compilează totul
dotnet test                                         # rulează testele
dotnet run --project src/Fidelizare.App             # pornește aplicația
```

## Producerea executabilului de livrat

```powershell
dotnet publish src/Fidelizare.App -c Release -o publish
```

Rezultă `publish/ProgramFidelizare.exe`, aproximativ 60 MB, care conține și
platforma .NET și motorul SQLite. Acesta este singurul fișier care trebuie
trimis magazinului.

---

## Prima pornire

La prima deschidere, programul cere numele magazinului și creează contul de
administrator. **Nu există o parolă implicită** — ar fi aceeași pentru toate
magazinele care primesc executabilul, iar cine o cunoaște ar putea modifica
solduri de puncte.

Baza de date se creează într-un subfolder `date`, lângă executabil. Dacă
programul a fost pus într-un loc fără drept de scriere (de obicei
`Program Files`), datele merg automat în folderul utilizatorului.

## Copii de siguranță

Toate datele magazinului stau într-un singur fișier. Copierea lui este o copie
de siguranță completă. Aplicația are un buton dedicat în **Administrare →
Sistem**, care închide întâi conexiunile, ca fișierul să nu fie surprins la
mijlocul unei scrieri.

## Mai multe case de marcat

Implicit programul lucrează cu o bază de date locală. Pentru două sau mai multe
case care trebuie să vadă aceiași clienți, se pune un fișier `fidelizare.config`
lângă executabil:

```
database = \\server\fidelizare\fidelizare.db
```

Programul detectează singur că fișierul este pe rețea și trece pe un mod de
jurnalizare potrivit pentru asta. Detalii și limitări în
[notele tehnice](docs/note-tehnice.md).
