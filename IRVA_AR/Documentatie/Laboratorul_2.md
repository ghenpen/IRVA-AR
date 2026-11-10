# Laboratorul 2 - Masurarea distantelor si mutarea punctelor

**Implementat:** puncte plasate pe plane, segmente intre puncte consecutive, distante pe segmente, distanta totala si bonusul de selectie/mutare prin doua atingeri.

**Scena:** [L2_MeasureDistancesApp.unity](../Assets/Scenes/L2_MeasureDistancesApp.unity)  
**Cod:** [MeasureDistances.cs](../Assets/Scripts/MeasureDistances.cs)

Prerechizite: manageri AR, tracking si coordonate world/local, explicate in [laboratorul 1](Laboratorul_1.md).

## 1. Ce trebuie sa poti spune din prima

> Ating ecranul, proiectez atingerea pe un plan detectat si creez o ancora in punctul rezultat. Cubul este copilul ancorei, deci urmeaza pozitia actualizata de tracking. Intre puncte consecutive construiesc segmente si calculez distanta euclidiana. Etichetele sunt elemente UI proiectate din pozitiile 3D ale mijloacelor segmentelor. Bonusul permite selectarea unui cub si mutarea lui pe alt punct al unui plan, pastrand aceleasi puncte si recalculand masuratorile.

**Nu este masurare intre pixeli.** Atingerea este in 2D, dar capetele segmentelor sunt pozitii 3D estimate de AR.

## 2. Componentele si resursele folosite

Scriptul este pe obiectul care contine managerii necesari. Atributul:

```csharp
[RequireComponent(typeof(ARRaycastManager), typeof(ARPlaneManager), typeof(ARAnchorManager))]
```

exprima dependenta fata de:

| Componenta | Rol |
|---|---|
| `ARRaycastManager` | Gaseste intersectia dintre atingerea ecranului si suprafetele AR. |
| `ARPlaneManager` | Obtine planul concret identificat de rezultatul raycast-ului. |
| `ARAnchorManager` | Creeaza ancora atasata planului. |
| Camera AR | Transforma intre coordonate de ecran si lumea 3D. |
| `EventSystem` + Input System | Intrare tactila si identificarea interactiunilor cu UI. |
| Canvas + TMP | Etichetele segmentelor, totalul si instructiunile. |

Resursele importante:

- [AR Placed Cube.prefab](../Assets/Prefabs/AR%20Placed%20Cube.prefab): markerul punctului; are renderer si collider pentru selectie.
- [Measurement Line.prefab](../Assets/Prefabs/Measurement%20Line.prefab): cub unitar scalat pentru a deveni un segment subtire.
- [Text (TMP).prefab](../Assets/Prefabs/Text%20%28TMP%29.prefab): eticheta unei distante.

**Linia nu este un `LineRenderer` in implementarea aceasta.** Este un obiect 3D scalat si orientat.

Prefab-urile optionale ale managerilor de raycast si ancore nu trebuie confundate cu markerul. Codul instantiaza explicit cubul sub ancora; nu este nevoie de un al doilea cub generat prin `Anchor Prefab`.

## 3. Cum este organizata starea in cod

| Camp | Continut |
|---|---|
| `points` | Cuburile, in ordinea plasarii. |
| `anchors` | Ancora asociata fiecarui punct, la acelasi index. |
| `lines` | Segmentele dintre perechile consecutive de puncte. |
| `distances` | Etichetele segmentelor, in aceeasi ordine ca liniile. |
| `spawnedObject` | Ultimul marker creat; nu inlocuieste lista tuturor punctelor. |
| `hits` | Buffer reutilizat pentru rezultatele raycast-ului AR. |
| `uiHits` | Buffer pentru rezultatele testarii UI. |
| `selectedPointIndex` | Indexul punctului selectat; `-1` inseamna nicio selectie. |
| `selectedRenderer` | Rendererul caruia i se aplica evidentierea. |
| `originalSelectionProperties` | Proprietatile de randare anterioare selectiei. |

Pentru `n` puncte:

```text
numar ancore = n
numar segmente = max(0, n - 1)
numar etichete = max(0, n - 1)
```

Segmentul `i` leaga `points[i]` de `points[i + 1]`. Mutarea nu schimba ordinea punctelor.

`readonly List<...>` inseamna ca referinta listei nu este reatribuita; continutul listei poate fi in continuare modificat cu `Add`, `Clear` etc.

## 4. Fluxul unei atingeri

```text
Update
    -> exista o atingere noua?
    -> atingerea este pe UI? Daca da, nu plasez
    -> am atins un cub existent? Selectez/schimb/anulez selectia
    -> altfel verific trackingul si subsistemul de ancore
    -> raycast pe PlaneWithinPolygon
    -> obtin ARPlane
    -> creez ancora
    -> fara selectie: AddPoint
    -> cu selectie: MoveSelectedPoint
```

Ordinea este importanta: cuburile existente au prioritate fata de plasarea pe plan.

### 4.1. `Awake()` - initializare

- Obtine cei trei manageri prin `GetComponent`.
- Verifica referintele la camera, prefab-uri, Canvas si textele HUD.
- Verifica existenta unui collider si a unui renderer in prefab-ul punctului.
- Initializeaza totalul la zero si afiseaza instructiunile.
- La o configuratie incompleta, raporteaza eroarea si dezactiveaza scriptul.

### 4.2. `TryGetTouchPosition(out Vector2 position)` - intrarea

Foloseste noul Input System:

```csharp
Touchscreen.current.primaryTouch.press.wasPressedThisFrame
```

Acest lucru detecteaza inceputul atingerii, nu fiecare cadru in care degetul ramane pe ecran. Altfel, o singura atingere lunga ar putea crea foarte multe puncte.

Parametrul `out` returneaza pozitia odata cu rezultatul boolean. Conventia `Try...` inseamna: incearca operatia, indica daca a reusit si ofera valoarea numai daca este disponibila.

In Editor exista si o ramura pentru mouse, protejata de `#if UNITY_EDITOR`. Aceasta nu inlocuieste trackingul AR real pe dispozitiv.

### 4.3. `IsPointerOverUI()` - protejarea meniului

Foloseste `EventSystem.RaycastAll` pentru pozitia atingerii. Daca UI-ul intercepteaza atingerea, `Update` nu continua spre plasare.

Etichetele generate au `raycastTarget = false`, astfel incat textul distantelor sa nu blocheze inutil atingerea mediului.

### 4.4. Raycast-ul AR si ancora

Operatiile principale sunt cele de mai jos. Fragmentul este simplificat; in script, fiecare pas este protejat de verificarile de succes si de existenta a planului/ancorei.

```csharp
raycastManager.Raycast(position, hits, TrackableType.PlaneWithinPolygon);
ARPlane plane = planeManager.GetPlane(hits[0].trackableId);
ARAnchor anchor = anchorManager.AttachAnchor(plane, hits[0].pose);
```

- `position`: coordonate 2D pe ecran.
- `PlaneWithinPolygon`: accepta o intersectie in interiorul conturului detectat al planului, nu oriunde pe extensia matematica infinita a lui.
- `hits[0]`: primul rezultat returnat; este folosit pentru plasare.
- `trackableId`: identifica planul din manager.
- `pose`: pozitie si orientare ale intersectiei.

**De ce ancora?** Un simplu `Vector3` salvat nu este o legatura activa cu trackingul. Ancora permite providerului sa actualizeze pozitia estimata a punctului.

Nu inseamna ca ancora garanteaza lipsa erorii sau a drift-ului. Masurarea ramane dependenta de tracking.

## 5. `AddPoint()` - ce se creeaza

1. Adauga ancora in lista.
2. Instantiaza markerul ca fiu al ancorei.
3. Ii seteaza pozitia locala la zero si rotatia locala la identitate.
4. Adauga markerul in `points`.
5. Daca exista deja un punct anterior, creeaza o linie si o eticheta.
6. Actualizeaza masuratorile.

Pozitia locala zero inseamna **in originea ancorei**, nu in originea lumii Unity.

Linia este copilul punctului anterior. Nu primeste o ancora separata: este calculata din cele doua capete, iar o ancora proprie ar putea produce o miscare independenta nedorita.

Eticheta este copilul Canvas-ului. Ancorele sale UI sunt centrate pentru pozitionarea folosita ulterior.

## 6. Matematica din `UpdateMeasurements()`

Metoda este chemata la plasare/mutare si in fiecare `LateUpdate`.

### 6.1. Distanta unui segment

Pentru punctele `A` si `B`:

```text
distanta = sqrt((Bx-Ax)^2 + (By-Ay)^2 + (Bz-Az)^2)
```

In cod:

```csharp
float distance = Vector3.Distance(start, end);
```

Este distanta euclidiana 3D. Nu este distanta pe ecran si nici distanta masurata de-a lungul unei suprafete curbate.

### 6.2. Mijlocul segmentului

```csharp
Vector3 midpoint = (start + end) * 0.5f;
```

Este folosit atat pentru pozitia liniei, cat si pentru eticheta.

### 6.3. Lungimea si orientarea liniei

```csharp
line.position = midpoint;
line.localScale = new Vector3(LineWidth, LineWidth, distance);
line.LookAt(end);
```

- `LineWidth = 0.005f`: grosime nominala de 5 mm.
- Lungimea este pe axa locala Z a cubului unitar.
- `LookAt(end)` orienteaza acea axa spre capatul segmentului.
- Pozitionarea in mijloc face ca obiectul sa se intinda intre capete.
- Pentru lungime aproape zero, linia este ascunsa si nu este apelat `LookAt`.

Interpretarea in metri presupune scara AR si ierarhia obiectelor pastrate corect. Scalarea arbitrara a originii sau a parintilor poate altera masurarea ori aspectul liniei.

### 6.4. Totalul

```text
total = distanta(P0, P1) + distanta(P1, P2) + ... + distanta(Pn-2, Pn-1)
```

Este lungimea traseului poligonal deschis, nu distanta directa dintre primul si ultimul punct si nici un perimetru inchis automat.

Exemplu conceptual: pentru segmente de 0,30 m si 0,40 m, totalul este 0,70 m. Daca segmentele sunt perpendiculare, distanta directa dintre primul si ultimul punct ar fi 0,50 m, dar aceasta nu este valoarea afisata ca total.

### 6.5. De ce recalculam in fiecare cadru?

Trackingul poate rafina pozitia ancorelor chiar daca utilizatorul nu mai atinge ecranul. Daca liniile ar fi calculate doar la plasare, ar putea ramane decalate fata de cuburi.

Costul creste aproximativ liniar cu numarul segmentelor. Pentru acest laborator, parcurgerea tuturor este simpla si permite actualizarea tuturor pozitiilor rafinate de tracking.

## 7. `UpdateLabelPosition()` - de la lumea 3D la UI

```text
midpoint in world
    -> Camera.WorldToScreenPoint
    -> verificare fata/spate si limite ecran
    -> ScreenPointToLocalPointInRectangle
    -> anchoredPosition pe Canvas
```

Pasii:

1. Proiecteaza pozitia 3D folosind camera AR.
2. Verifica adancimea fata de planele near/far si limitele ecranului.
3. Ascunde eticheta daca punctul nu este in zona vizibila verificata.
4. Converteste coordonatele de ecran in coordonate locale ale Canvas-ului.
5. Adauga un offset vertical de `24` unitati UI.

Pentru `ScreenSpaceOverlay`, camera UI folosita in conversie este `null`. Pentru alte moduri se foloseste `parent.worldCamera`.

Eticheta este UI suprapus: verificarea nu calculeaza ocluzia fata de obiectele reale din camera.

## 8. Bonusul - selectare si mutare

### 8.1. `FindPointAt()` foloseste alt tip de raycast

Pentru cuburi:

```csharp
Ray ray = FirstPersonCamera.ScreenPointToRay(screenPosition);
collider.Raycast(ray, out RaycastHit hit, nearestDistance);
```

Se testeaza collider-ele punctelor existente si se pastreaza intersectia cea mai apropiata. `Physics.SyncTransforms()` sincronizeaza transformarile inaintea testului.

**Diferenta de explicat:**

- Raycast AR: intersectie cu un plan detectat de provider.
- Raycast pe collider: selectie de geometrie virtuala existenta in Unity.

### 8.2. Selectarea si anularea

`SelectPoint()`:

- elimina evidentierea anterioara;
- retine indexul si rendererul;
- salveaza proprietatile initiale;
- aplica galben prin `MaterialPropertyBlock`.

Nu modifica materialul partajat al prefab-ului, deci celelalte cuburi nu devin si ele galbene.

`ClearSelection()` restaureaza proprietatile si seteaza indexul la `-1`.

Interactiunea:

| Atingere | Efect |
|---|---|
| Cub fara selectie curenta | Selecteaza cubul. |
| Acelasi cub selectat | Anuleaza selectia. |
| Alt cub | Schimba selectia. |
| Plan valid, cu un cub selectat | Muta acel cub. |
| Plan valid, fara selectie | Adauga un punct nou. |

### 8.3. `MoveSelectedPoint()` - de ce ordinea este importanta

Noua ancora a fost deja creata cu succes in `Update` inainte sa intre aici.

1. Retine ancora veche.
2. Reataseaza markerul sub ancora noua.
3. Reseteaza pozitia si rotatia locala.
4. Actualizeaza referinta din `anchors`.
5. Elimina selectia.
6. Distruge ancora veche.
7. Recalculeaza masuratorile.

**Nu se distruge intai ancora veche.** Markerul si linia lui sunt copii ai ierarhiei vechi; trebuie mutati inainte ca acea ierarhie sa fie eliminata.

Daca raycast-ul sau crearea noii ancore esueaza, punctul initial si selectia raman. Nu se pierde masuratoarea doar pentru ca mutarea nu a fost valida.

La mutarea unui punct interior se modifica cele doua segmente adiacente. Codul recalculeaza toate segmentele, dar nu adauga cuburi, linii sau etichete noi.

## 9. Pierderea ancorelor si eliberarea resurselor

- Daca un marker a fost distrus odata cu ancora, `UpdateMeasurements` observa valoarea `null`, raporteaza situatia si reseteaza masuratorile.
- Asta nu este acelasi lucru cu o pierdere temporara a trackingului in care obiectul continua sa existe.
- `OnDisable` elimina selectia si evidentierea.
- `OnDestroy` cheama `ReleaseMeasurements`.
- `ReleaseMeasurements` distruge etichetele si ancorele, apoi goleste listele.
- Cuburile si liniile dispar impreuna cu ierarhiile ancorelor; nu este nevoie sa ramana orfane in scena.

## 10. Cum faci demonstratia

1. Detectezi o suprafata.
2. Plasezi trei puncte: trebuie sa vezi doua segmente, doua etichete si un total.
3. Explici ca totalul este suma celor doua lungimi.
4. Misti camera: etichetele trebuie sa urmareasca proiectia mijloacelor segmentelor.
5. Selectezi punctul din mijloc: devine galben.
6. Atingi un alt loc pe plan: punctul se muta si se actualizeaza ambele segmente.
7. Selectezi un punct si il atingi din nou: selectia se anuleaza.
8. Arati ca atingerea unui control UI nu adauga un punct.

Pentru o comparatie fizica foloseste doua repere pe o masa si o rigla. Explica faptul ca este o estimare AR, nu un instrument metrologic certificat.

## 11. Intrebari posibile

**De ce folosesti ancore si nu numai pozitii?**  
Pentru ca punctele trebuie sa ramana legate de estimarea spatiala actualizata de AR.

**De ce ai doua raycast-uri diferite?**  
Planul real detectat este interogat prin ARRaycastManager; cubul virtual este selectat prin collider.

**De ce ai `n - 1` segmente?**  
Conectez doar fiecare punct la precedentul, fara sa inchid traseul.

**Ce se intampla daca mut punctul din mijloc?**  
Ramane acelasi punct in lista. Cele doua distante adiacente si totalul se recalculeaza.

**De ce etichetele nu sunt puse direct la pozitia 3D?**  
Sunt elemente UI ale Canvas-ului, deci pozitia world trebuie proiectata si convertita.

**De ce doua zecimale nu inseamna precizie de un centimetru?**  
Formatul textului controleaza afisarea. Eroarea trackingului este independenta de numarul de zecimale.

**De ce folosesti `MaterialPropertyBlock`?**  
Pentru evidentiere pe un singur renderer, fara modificarea materialului partajat de toate cuburile.

## 12. Varianta de prezentare in aproximativ un minut

> Laboratorul transforma o atingere 2D intr-un punct 3D prin raycast pe un plan AR. Creez o ancora si un cub copil al ei. Listele retin punctele, ancorele, liniile si etichetele. Pentru fiecare pereche consecutiva calculez distanta cu Vector3.Distance, pun linia la mijloc, o scalez pe Z si o orientez spre capat. Totalul este suma segmentelor. Proiectez mijloacele pe Canvas pentru afisarea etichetelor si recalculez in LateUpdate pentru a urmari ancorele. Bonusul selecteaza cubul prin collider, il evidentiaza fara sa modifice materialul comun si il muta numai dupa ce noua ancora a fost creata.

### Checklist

- [ ] Pot urmari un tap de la Input System pana la cubul ancorat.
- [ ] Pot diferentia raycast-ul AR de selectia prin collider.
- [ ] Pot explica distanta, mijlocul, orientarea si totalul.
- [ ] Stiu de ce liniile nu au ancore independente.
- [ ] Pot explica transformarea world -> screen -> Canvas.
- [ ] Stiu de ce recalcularea se face si in `LateUpdate`.
- [ ] Pot explica de ce noua ancora este creata inainte de distrugerea celei vechi.
- [ ] Stiu ce se intampla la selectie, anulare, esecul mutarii si distrugerea ancorei.

**Navigare:** [Laboratorul 1](Laboratorul_1.md) | [Laboratorul 3](Laboratorul_3.md).
