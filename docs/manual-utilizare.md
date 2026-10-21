# Manual de utilizare

Acest manual este scris pentru personalul magazinului. Nu presupune cunoștințe
tehnice.

---

## 1. Prima pornire

La prima deschidere, programul vă conduce prin doi pași.

### Pasul 1 — magazinul și administratorul

- **numele magazinului** și **numele programului de fidelizare** — apar pe ecrane
  și pe fereastra de autentificare;
- **contul de administrator** — numele dumneavoastră și parola.

Numele este unul singur: cu el vă autentificați și tot el apare în rapoarte și în
jurnal. Scrieți-l cum vreți să fie citit — „Cristian Popescu", nu „cristi".
Spațiile și diacriticele sunt permise.

Parola trebuie să aibă cel puțin 8 caractere, cu cel puțin o literă și o cifră.

> **Important:** nu există nicio cale de recuperare a parolei din afara
> programului. Păstreaz-o într-un loc sigur. Dacă se pierde parola unui casier,
> administratorul o poate reseta; dacă se pierde parola administratorului, nu.

### Pasul 2 — conturile personalului

Aici creați conturile cu care se lucrează efectiv: casierii și, dacă e cazul,
responsabilii. Completați numele, rolul și parola, apoi apăsați **Adaugă contul**.
Puteți adăuga oricâte, unul după altul — fiecare apare în lista de sub formular.

Ca și la administrator, numele este unul singur. „Ana Popescu" este și numele cu
care se autentifică, și cel care va apărea în dreptul fiecărei vânzări făcute de ea.

> **Contul de administrator nu este contul de lucru.** El configurează programul.
> Dacă toată lumea lucrează cu el, jurnalul va scrie „Administrator” la fiecare
> vânzare și la fiecare corecție de puncte, iar întrebarea „cine a făcut asta?”
> rămâne fără răspuns.

Puteți sări peste acest pas — programul vă avertizează, iar pe ecranul Casă
rămâne o bandă portocalie până când creați primul cont de lucru. Conturile se pot
crea oricând și mai târziu, din **Administrare → Utilizatori**.

Baza de date se creează automat, într-un folder numit `date`, lângă program.

---

## 2. Conturi și roluri

Fiecare persoană care lucrează cu programul trebuie să aibă propriul cont.
Nu este birocrație: programul reține cine a înregistrat fiecare vânzare, cine a
acordat fiecare recompensă și cine a modificat solduri. Cu un cont comun,
această informație nu ar valora nimic.

| Rol | Ce poate face |
|---|---|
| **Casier** | Caută clienți, îi înscrie, înregistrează vânzări, acordă recompense. |
| **Responsabil** | În plus: corecții de puncte, anulări de vânzări și recompense, rapoarte, catalogul de recompense. |
| **Administrator** | În plus: regulile programului, nivelurile, conturile personalului, copiile de siguranță. |

Conturile se creează **oricând**, nu doar la prima pornire. Aveți trei drumuri
către ele:

- la **pasul 2** al primei porniri;
- din butonul **„Conturile personalului”**, în colțul din stânga jos, sub numele
  dumneavoastră — vizibil doar pentru administrator;
- din **Administrare → Utilizatori → Cont nou**.

Toate trei duc în același loc. Acolo puteți adăuga oricâte conturi, oricând:
un casier nou angajat, un coleg care intră în tură, un responsabil de schimb.
Tot de acolo se modifică rolul cuiva, se dezactivează un cont când persoana
pleacă și se resetează parolele uitate.

> **Doi angajați cu același nume.** Numele trebuie să fie unic, fiindcă este și
> cel de autentificare. Dacă aveți două Ana Popescu, a doua va fi „Ana Popescu 2"
> sau „Ana P. tura 2" — programul refuză duplicatul și vă spune de ce.

Ce vede fiecare rol în meniul din stânga:

| Rol | Meniu |
|---|---|
| **Casier** | Casă, Clienți, Rapoarte |
| **Responsabil** | În plus: Administrare — fără filele Utilizatori și Sistem |
| **Administrator** | Tot, inclusiv conturile și copiile de siguranță |

Când administratorul resetează parola cuiva, acea persoană este obligată să își
aleagă o parolă proprie la următoarea intrare — astfel parola stabilită de
altcineva nu rămâne în folosință.

---

## 3. Ecranul Casă — activitatea de zi cu zi

Este ecranul pe care îl veți folosi aproape tot timpul. Se lucrează de sus în
jos, în ordinea în care se întâmplă lucrurile la tejghea.

### Pasul 1 — găsirea clientului

În câmpul mare de sus puteți:

- **scana cardul** — cititorul tastează codul și apasă singur Enter, deci
  clientul apare fără să atingeți nimic;
- **tasta numărul de telefon** — merge și cu spații: `0722 123 456` și
  `0722123456` sunt același număr;
- **căuta după nume** — diacriticele nu contează. Căutarea după `taranu
  gheorghita` găsește clientul `Țăranu Gheorghiță`.

Dacă se potrivesc mai mulți clienți, apare o listă din care îl alegeți pe cel
corect. Dacă nu se găsește nimeni, apăsați **Client nou**.

### Pasul 2 — citirea fișei

După identificare apare cartonașul clientului:

- **numele, cardul și datele de contact**;
- **nivelul** — eticheta colorată din dreapta (Bronze, Silver, Gold);
- **punctele disponibile**, cu echivalentul aproximativ în lei;
- **bara de progres** — cât mai are de cheltuit până la nivelul următor;
- **avertismentul portocaliu**, dacă are puncte care expiră în curând. Spuneți-i
  clientului: sunt puncte pe care le pierde dacă nu le folosește;
- **banda verde**, dacă își serbează zilele. Apăsați **Acordă bonusul** — se
  poate acorda o singură dată pe an.

### Pasul 3 — înregistrarea vânzării

Completați **valoarea bonului**. Se acceptă atât virgula, cât și punctul:
`199,90` și `199.90` înseamnă același lucru.

- **Reducere** — dacă ați acordat o reducere comercială, treceți-o aici.
  Punctele se calculează pe suma efectiv încasată.
- **Număr bon** — opțional, dar util: leagă operațiunea de bonul fiscal și
  împiedică înregistrarea aceleiași vânzări de două ori.

Sub câmpuri vedeți, înainte de a apăsa ceva, **cât se încasează** și **câte
puncte va primi clientul**, inclusiv bonusul de nivel.

Apăsați **Înregistrează vânzarea**, sau pur și simplu Enter. Mesajul verde
confirmă câte puncte a primit și ce sold are acum.

### Pasul 4 — acordarea unei recompense

În coloana din dreapta este catalogul. Fiecare recompensă arată costul în puncte
și beneficiul. Cele care nu pot fi folosite acum sunt gri, cu motivul scris
dedesubt: „Mai sunt necesare 40 de puncte”, „Valoarea minimă a bonului este 150
lei”, „Recompensa este rezervată nivelului Gold”.

> **Completați întâi valoarea bonului.** Recompensele cu prag minim se activează
> abia după ce programul știe cât costă coșul.

După confirmare se întâmplă trei lucruri deodată:

1. **Punctele se scad pe loc** din contul clientului.
2. **Reducerea se scrie singură** în câmpul „Reducere”. Nu o retastați — a fost
   deja trecută. O bandă albastră vă reamintește să o aplicați **și pe casa de
   marcat**, unde programul nu ajunge.
3. Apare butonul de anulare, despre care mai jos.

Pentru un produs gratuit nu se completează nicio reducere: programul vă spune
doar să predați produsul.

Abia după aceea apăsați **Înregistrează vânzarea**. Punctele noi se calculează pe
suma efectiv încasată, deci reducerea obținută din puncte nu produce puncte noi.

> **Ordinea contează.** Acordați recompensa *înainte* de a înregistra vânzarea.
> Invers, punctele s-ar calcula pe suma întreagă, iar reducerea nu ar mai avea
> unde să se aplice.

### Am acordat recompensa greșită

Se întâmplă: lista are mai multe recompense, iar clicul alunecă. Imediat după
acordare apare o bandă portocalie cu butonul **„Anulează «numele recompensei»”**.

Apăsați-l și totul se întoarce: punctele revin la client, reducerea se scoate din
bon. Nu trebuie chemat responsabilul.

Butonul este disponibil **10 minute** (interval reglabil din Administrare →
Reguli) și doar pentru recompensa acordată de dumneavoastră. Dispare în momentul
în care înregistrați vânzarea — de acolo încolo bonul poartă deja reducerea, iar
corectura înseamnă anularea vânzării, care este operațiune de responsabil.

---

### Clientul returnează produse (responsabil sau administrator)

Butonul **„Retur”**, sus pe ecranul Casă, lângă „Client nou”:

1. Tastați sau scanați **numărul bonului** și apăsați Enter. Apar clientul, suma
   bonului, punctele primite și ce s-a returnat deja din el.
2. Introduceți **valoarea produselor returnate**, așa cum apare pe documentul de
   retur al casei de marcat. Dacă se returnează tot, apăsați **„Tot bonul”**.
   Sub sumă vedeți dinainte câte puncte se vor retrage.
3. Scrieți **motivul** (ce s-a returnat, numărul documentului de retur) și apăsați
   **„Înregistrează returul”**. Clientul apare apoi pe ecran, cu soldul nou.

Câteva lucruri bune de știut:

- Punctele se retrag **proporțional** cu suma returnată. Dintr-un bon de 1000 de
  lei cu 100 de puncte, un retur de 400 de lei retrage 40 de puncte.
- Se pot face **mai multe retururi pe același bon**, câte unul la fiecare vizită
  a clientului, până la valoarea bonului. Împreună nu retrag niciodată mai multe
  puncte decât s-au acordat.
- Suma returnată **iese și din rulajul pentru nivel**: un client nu rămâne Gold
  pe bani returnați.
- Dacă clientul a cheltuit deja punctele, soldul coboară la zero, dar nu devine
  negativ, iar programul vă spune câte puncte nu au mai putut fi retrase.
- Returul cere numărul bonului. Dacă vânzarea a fost trecută fără număr, returul
  se face din fișa clientului (vezi mai jos).

---

## 4. Înscrierea unui client nou

Apăsați **Client nou**. Obligatorii sunt doar numele și prenumele.

- **Telefonul** este a doua cale de identificare la casă. Merită completat: dacă
  clientul își uită cardul, îl găsiți după telefon.
- **Data nașterii** este necesară pentru bonusul aniversar.
- **Codul de card** se completează singur cu următorul număr liber. Îl puteți
  scrie manual dacă folosiți carduri deja tipărite.
- **Acordul pentru comunicări comerciale** se bifează doar dacă a fost exprimat
  efectiv. Punctele și recompensele funcționează și fără el.

La înscriere, clientul primește imediat bonusul de bun venit.

---

## 5. Ecranul Clienți

Lista completă, cu nivel, puncte, rulaj și data ultimei cumpărături.

Dublu clic pe un rând deschide **fișa completă**, unde găsiți:

- situația curentă și rulajul;
- toate **cumpărările**;
- toate **recompensele acordate**;
- **istoricul punctelor** — fiecare mișcare, cu data, motivul, soldul rezultat și
  operatorul.

Fila „Istoricul punctelor” este răspunsul la întrebarea *„de ce am atâtea
puncte?”*. Arată tot drumul, nu doar rezultatul.

### Corecții și anulări (responsabil sau administrator)

- **Corecție de puncte** — adaugă sau scade puncte manual. Motivul este
  obligatoriu și rămâne în istoric.
- **Retur din cumpărarea selectată** — același retur ca pe ecranul Casă, pornit
  de la rândul ales în fila „Cumpărări”. Coloana „Stare” arată cât s-a returnat
  din fiecare bon.
- **Anularea unei cumpărări** — selectați rândul în fila „Cumpărări”, apoi
  apăsați butonul. Punctele acordate se retrag. Dacă între timp clientul le-a
  cheltuit, soldul coboară la zero, dar nu devine negativ — programul vă
  avertizează câte puncte nu au putut fi retrase.
- **Anularea unei recompense** — punctele se întorc în pachetele din care au fost
  luate, cu datele lor originale de expirare. Punctele ale căror pachete au
  expirat între timp nu se mai restituie, iar programul vă spune câte au fost.

---

## 6. Ecranul Rapoarte

**Starea programului** — câți clienți sunt înscriși, câți au cumpărat în
ultimele 90 de zile, câte puncte nefolosite există și distribuția pe niveluri.

> „Puncte nefolosite” arată și o valoare în lei. Este suma pe care magazinul o
> datorează clienților sub formă de reduceri viitoare. Merită urmărită: dacă
> crește necontrolat, regulile sunt prea generoase.

**Perioada selectată** — încasări, număr de bonuri, bon mediu, puncte acordate,
puncte folosite, puncte expirate. Se poate alege ziua curentă, ultimele 7 sau 30
de zile, luna sau anul.

**Clienți cu cel mai mare rulaj** — cine merită atenție specială.

**Operațiuni recente** — jurnalul corecțiilor, anulărilor și modificărilor de
reguli, cu autorul fiecăreia.

---

## 7. Ecranul Administrare

### Reguli

Tot ce ține de mecanica programului:

| Setare | Ce înseamnă |
|---|---|
| Lei pentru un punct | Cu 10, un bon de 250 lei aduce 25 de puncte. |
| Valoarea minimă a bonului | Sub această sumă nu se acordă puncte. Zero = fără prag. |
| Valoarea unui punct | Folosită pentru estimările afișate și pentru calculul sumei datorate. |
| Prag minim de revendicare | Sub acest sold nu se poate revendica nimic. |
| Casierul își poate anula recompensa (minute) | Cât timp după acordare casierul își corectează singur greșeala. Zero = orice anulare trece prin responsabil. |
| Valabilitate (luni) | După câte luni expiră punctele. Zero oprește expirarea. |
| Avertizare înainte de expirare | Cu câte zile înainte apare avertismentul la casă. |
| Bonus la înscriere / de zi de naștere | Zero dezactivează bonusul. |
| Perioada de calificare | Pe câte luni se numără rulajul pentru nivel. Zero = tot istoricul, iar nivelul nu mai scade. |

Modificările se aplică de la următoarea operațiune. **Punctele deja acordate își
păstrează termenul de expirare stabilit atunci** — o schimbare de reguli nu
scurtează retroactiv valabilitatea punctelor deja câștigate.

### Niveluri și Recompense

**Nivelurile sunt în întregime ale dumneavoastră.** Bronze, Silver și Gold sunt
doar ce găsiți la prima pornire. Le puteți șterge și înlocui cu ale magazinului:
două niveluri, cinci, cu ce denumiri vreți — „Client”, „Client fidel”, „VIP” —
cu ce praguri și ce multiplicatori vreți.

Singura regulă: nivelul cel mai de jos trebuie să pornească de la 0, altfel un
client fără cumpărături nu s-ar încadra nicăieri.

În fila **Reguli** alegeți și **după ce se calculează** nivelul:

- **rulajul în lei** — cât a cheltuit clientul (implicit);
- **punctele acumulate** — cât a strâns, indiferent dacă le-a și folosit;
- **numărul de cumpărături** — de câte ori a venit, indiferent cât a cheltuit.

> Atenție: schimbarea bazei schimbă înțelesul pragurilor. Un prag de 1.000
> înseamnă altceva în lei decât în puncte. După ce o modificați, treceți prin
> fila Niveluri și verificați pragurile.

Dacă vreți doar să retrageți temporar o recompensă, nu o ștergeți — modificați-o
și debifați „Activă”.

### Campanii

Perioade în care punctele se acordă înmulțit. Fiecare campanie are o denumire, o
perioadă, un multiplicator și, opțional, o valoare minimă a bonului.

Exemple de folosit:

- **puncte duble de Black Friday** — perioadă de două zile, multiplicator 2;
- **luna cadourilor** — 1–31 decembrie, multiplicator 1,5;
- **marțea clienților** — o perioadă lungă, bifând doar „Marți”, multiplicator 2.

Campania se adaugă **peste** bonusul de nivel: un client Gold cu ×1,5, într-o
campanie ×2, primește de trei ori punctele de bază. La casă, casierul vede o
bandă verde care anunță campania și punctele exacte, înainte de a încasa.

Dacă din greșeală suprapuneți două campanii, se aplică cea mai generoasă, nu
amândouă. Este o plasă de siguranță: două campanii ×2 suprapuse ar fi însemnat
×4, adică o greșeală vizibilă abia în raportul de la sfârșitul lunii.

### Câmpuri client

Aici adăugați informațiile în plus pe care vreți să le rețineți despre clienți:
mărime preferată, oraș, stil, cum a aflat de magazin.

Fiecare câmp are un tip — text, număr, dată, da/nu sau listă de opțiuni — și
trei bife:

- **obligatoriu** — clientul nu poate fi salvat fără el;
- **căutabil** — valoarea intră în căutarea rapidă de la casă. Puteți găsi
  clientul tastând „brasov” dacă ați reținut orașul;
- **în listă** — apare ca o coloană în ecranul Clienți.

Câmpurile apar automat în formularul de client, fără nicio altă intervenție.

> Ștergerea unui câmp șterge definitiv valorile completate pentru toți clienții.
> Dacă vreți doar să nu mai apară, debifați „Câmp activ” — valorile rămân.

### Identitate

**Culoarea programului** — alegeți din paletă sau scrieți codul. Se aplică
imediat, pe tot programul, fără repornire.

**Logo** — apare în bara laterală și pe ecranul de autentificare. Imaginea se
păstrează în baza de date, deci intră automat în copiile de siguranță.

**Formatul codului de card** — prefix și număr de cifre. Dacă aveți deja carduri
tipărite de forma `MAG-0001`, puneți prefixul `MAG-` și 4 cifre. Se aplică doar
codurilor generate de acum înainte; cardurile deja emise rămân neschimbate.

### Sistem

- **Copie de siguranță** — salvează întreaga bază de date într-un fișier ales de
  dumneavoastră. Faceți-o periodic, pe o memorie externă sau într-un folder
  sincronizat.
- **Verifică punctele expirate acum** — se face automat la fiecare pornire.
  Butonul este util dacă programul rămâne deschis mai multe zile la rând.
- **Date demonstrative** — creează clienți inventați, ca să vedeți cum arată
  programul înainte de a avea clienți reali. Funcționează doar pe o bază goală.

### Import de clienți

Dacă aveți deja o listă de clienți — într-un Excel, într-un caiet trecut pe
calculator sau într-un program vechi — nu trebuie să îi introduceți manual.

1. Apăsați **Salvează fișierul model**. Obțineți un CSV cu antetul corect.
2. Completați-l în Excel, păstrând prima linie neschimbată. Coloanele
   recunoscute sunt: Nume, Prenume, Telefon, Email, DataNasterii, CodCard,
   Puncte, Observatii — plus câmpurile proprii, după numele lor scurt.
3. Apăsați **Importă clienți din CSV** și alegeți fișierul.

**Programul vă arată întâi ce s-ar întâmpla**, fără să scrie nimic: câți clienți
ar intra, câți ar fi ignorați și din ce motiv, ce coloane a recunoscut și pe care
le ignoră. Abia după ce confirmați se scrie ceva.

Rândurile problematice sunt raportate cu numărul liniei și sărite; restul intră
normal. Telefoanele și codurile de card duplicate sunt detectate atât față de
clienții existenți, cât și în interiorul fișierului.

> Clienții importați **nu** primesc bonusul de bun venit: ei existau deja înainte
> de program, iar un bonus acordat retroactiv le-ar umfla soldul. Dacă în fișier
> există o coloană `Puncte`, soldurile de acolo se preiau ca atare.

### Export de date

Trei butoane, fiecare scriind un CSV: **Clienți** (cu nivel, puncte, rulaj și
câmpurile proprii), **Cumpărături** și **Istoricul punctelor** — ultimele două pe
ultimele 12 luni.

Fișierele folosesc punct și virgulă ca separator, forma pe care Excel-ul cu
setări românești o deschide corect fără nicio conversie.

### Pornirea de la zero

Programul nu are un buton de ștergere a datelor, intenționat: într-un magazin în
funcțiune, o astfel de comandă apăsată din greșeală ar fi o catastrofă. Reluarea
de la zero se face manual, iar pașii sunt aceștia:

1. **Închideți programul.** Cât timp rulează, fișierele sunt blocate.
2. Mergeți în folderul unde stă executabilul. Acolo veți găsi un subfolder numit
   **`date`** (calea exactă apare în **Administrare → Sistem**).
3. **Redenumiți-l**, de exemplu în `date-vechi`. Ștergeți-l doar dacă sunteți
   sigur că nu mai aveți nevoie de nimic din el — redenumirea vă lasă o cale de
   întoarcere.
4. Porniți programul din nou. Nu găsește baza de date, o creează pe loc și vă
   cere din nou configurarea inițială.

Ce se pierde: **toți clienții, punctele, cumpărăturile, recompensele acordate și
conturile personalului.** Ce se recreează automat: structura bazei de date, cele
trei niveluri, catalogul implicit de șase recompense și regulile implicite.

> Folderul `date` conține trei fișiere: `fidelizare.db` și, uneori, `fidelizare.db-wal`
> și `fidelizare.db-shm`. Mutați-le pe toate odată, nu doar pe primul — dacă
> programul a fost închis forțat, o bună parte din date poate să se afle încă în
> fișierul `-wal`, nu în cel principal.

---

## 8. Întrebări frecvente

**Clientul și-a uitat cardul.**
Căutați-l după telefon sau după nume.

**Am greșit suma pe un bon deja înregistrat.**
Un responsabil anulează cumpărarea din fișa clientului, cu motiv, apoi o
înregistrați corect.

**Clientul a returnat un produs.**
Nu anulați vânzarea: folosiți butonul „Retur” de pe ecranul Casă, cu numărul
bonului și valoarea produsului returnat. Se retrag doar punctele aferente.

**Clientul spune că avea mai multe puncte.**
Deschideți fișa lui, fila „Istoricul punctelor”. Acolo scrie fiecare mișcare,
inclusiv punctele expirate, cu data și motivul.

**Un client are puncte, dar nicio recompensă nu se activează.**
Verificați dacă ați completat valoarea bonului — recompensele cu prag minim au
nevoie de ea. Verificați și pragul minim de revendicare din reguli.

**Se poate folosi pe două calculatoare?**
Da, dar necesită o configurare suplimentară. Vedeți secțiunea corespunzătoare
din `README.md`.

**Unde sunt datele?**
Într-un singur fișier, în folderul `date` de lângă program. Calea exactă apare
în **Administrare → Sistem**.
