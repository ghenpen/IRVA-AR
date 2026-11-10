# Laboratorul 3 - Cloud Anchors: Host, Delete si Resolve

**Implementat:** cerintele principale, cu un singur cub si un singur ID salvat. **Bonusul cu ancore multiple nu este inclus.**

**Scena:** [L3_CloudAnchors.unity](../Assets/Scenes/L3_CloudAnchors.unity)

**Cele doua scripturi:**

- [CloudAnchorObjectPlacement.cs](../Assets/Scripts/CloudAnchorObjectPlacement.cs): atingerea ecranului, plasarea, recrearea si stergerea cubului local.
- [ARCloudAnchorManager.cs](../Assets/Scripts/ARCloudAnchorManager.cs): calitatea hartii, Host, Resolve, persistenta ID-ului, starea operatiilor si UI-ul.

## 1. Ideea pe care trebuie sa o explici

> In laboratorul 2, ancora leaga un punct de trackingul sesiunii curente. Aici public o ancora prin serviciul Cloud Anchors, obtin un ID si il salvez local. Dupa ce sterg reprezentarea locala, cer rezolvarea acelui ID. Serviciul si trackingul folosesc observatiile mediului pentru relocalizare, iar eu recreez cubul sub ancora returnata.

**Cloud Anchors nu incarca modelul 3D al cubului.** Continutul vizual ramane in aplicatie. ID-ul permite regasirea referintei spatiale, iar aplicatia decide ce prefab sa afiseze acolo.

Nu este GPS si nu este doar salvarea unui `Vector3`. Coordonatele Unity ale unei sesiuni noi nu reprezinta automat acelasi reper fizic ca in sesiunea precedenta.

## 2. Termenii esentiali

| Termen | Ce inseamna aici |
|---|---|
| Ancora locala | Referinta spatiala creata pe un plan in sesiunea AR curenta. |
| Host | Operatia care publica ancora si informatia necesara relocalizarii prin serviciul cloud. |
| Cloud Anchor ID | Identificatorul returnat la Host reusit si folosit ulterior la Resolve. |
| Resolve | Operatia care incearca sa regaseasca ancora in mediul observat. |
| Feature map | Reprezentarea reperelor vizuale din mediu folosita pentru localizare. |
| Feature-map quality | Estimare a calitatii datelor disponibile pentru hosting, nu numarul de plane. |
| TTL | Durata de viata a ancorei publicate. In acest proiect: o zi. |
| Promise | Obiect SDK care reprezinta o operatie asincrona: in asteptare, finalizata sau anulata. |
| Coroutine | Metoda Unity care poate suspenda executia cu `yield`, permitand continuarea cadrelor. Nu este automat un fir de executie separat. |
| Relocalizare | Regasirea pozitiei spatiale fata de mediul observat anterior. |

AR Foundation ofera ancorele locale. Integrarea Cloud Anchors foloseste suplimentar Google ARCore Extensions si autorizarea serviciului.

## 3. Separarea responsabilitatilor

```text
Atingere pe suprafata UI transparenta
    -> CloudAnchorObjectPlacement
    -> raycast pe plan
    -> ancora locala + cub
    -> QueueAnchor

Buton Host / Resolve
    -> ARCloudAnchorManager
    -> verificari + cerere asincrona
    -> rezultat SDK
    -> salvare ID sau recrearea cubului
```

Separarea evita amestecarea selectiei punctului de plasare cu operatiile de retea si cu regulile de activare a butoanelor.

### Componentele scenei

- `ARSession`, `XROrigin` si camera AR din spate.
- `ARPlaneManager`, `ARRaycastManager`, `ARAnchorManager`.
- `ARCoreExtensions`, cu referinte la sesiune, origine si camera.
- Configuratia [ARCoreExtensionsCloudAnchorsConfig.asset](../Assets/ARCoreExtensionsCloudAnchorsConfig.asset), cu Cloud Anchor Mode activat.
- Canvas cu Host, Resolve, Delete, panoul de status si suprafata de atingere.

Autorizarea existenta este configuratie a proiectului, nu o cheie scrisa in aceste doua scripturi. Nu afisa si nu include in documentatie sau commit-uri cheia ori ID-uri reale.

## 4. Plasarea unui singur cub

Deschide [CloudAnchorObjectPlacement.cs](../Assets/Scripts/CloudAnchorObjectPlacement.cs).

### 4.1. `IPointerDownHandler` si `OnPointerDown()`

Clasa implementeaza `IPointerDownHandler`, deci primeste evenimente prin sistemul UI.

Suprafata transparenta care primeste atingerea este in spatele controalelor. Atingerea unui buton este destinata acelui control, nu si plasarii unui cub. Este o abordare diferita de citirea directa a touchscreen-ului din laboratorul 2.

Verificarile importante:

1. Este evenimentul principal acceptat?
2. Exista deja un cub? Daca da, nu plaseaza altul.
3. Controllerul cloud este activ?
4. Exista o operatie in curs? Daca da, pastreaza informatiile operatiei.
5. Sesiunea este in `SessionTracking`, iar subsistemul de ancore ruleaza?
6. Raycast-ul intersecteaza un plan valid?
7. Poate fi creata ancora atasata planului?

La succes:

```csharp
if (RecreatePlacement(anchor.transform))
    cloudManager.QueueAnchor(anchor);
```

Prima creeaza continutul local. A doua spune managerului cloud ca aceasta ancora este candidata pentru Host.

### 4.2. `RecreatePlacement(Transform resolvedAnchor)`

Metoda este folosita pentru ambele situatii:

- ancora locala obtinuta prin atingere;
- ancora returnata de Resolve.

Retine `anchorRoot`, creeaza [AR Placed Cube.prefab](../Assets/Prefabs/AR%20Placed%20Cube.prefab) ca fiu al acelei transformari si ii reseteaza pozitia/rotatia locala.

Nu creeaza o alta ancora langa cea rezolvata. Foloseste direct transformarea ancorei primite.

`HasPlacement` inseamna ca exista cubul local, nu ca exista neaparat un Host reusit.

### 4.3. `RemovePlacement()` si `ReleasePlacement()`

Butonul Delete:

1. refuza stergerea in timpul unei operatii cloud;
2. verifica daca exista un cub;
3. distruge ancora locala si ierarhia cubului;
4. anunta managerul ca nu mai exista o ancora locala pregatita pentru Host.

**Nu sterge ID-ul din `PlayerPrefs` si nu trimite o cerere de stergere a ancorei de pe server.**

`ReleasePlacement()` este folosita si in `OnDestroy`, la distrugerea componentei/scenei.

## 5. Starea din managerul cloud

Deschide [ARCloudAnchorManager.cs](../Assets/Scripts/ARCloudAnchorManager.cs).

| Camp/proprietate | Rol |
|---|---|
| `pendingHostAnchor` | Ancora locala care poate fi publicata; dupa Host reusit devine `null`. |
| `anchorIdToResolve` | Ultimul ID incarcat din preferinte sau primit la Host reusit. |
| `hostPromise` / `resolvePromise` | Operatia nativa in curs. |
| `operation` | Coroutine-ul care urmareste operatia. |
| `IsBusy` | Adevarat daca exista una dintre promisiunile active. |
| `HasSavedAnchor` | Exista un sir ID nevid; nu garanteaza ca ancora nu a expirat. |
| `mappingQuality` | Ultima estimare valida; este nullable, deci poate fi indisponibila. |
| `resolveDeadline` | Momentul limita al incercarii curente de Resolve. |
| `statusMessage` | Mesajul ultimei actiuni/operatii. |
| `trackingMessage` / `qualityMessage` | Telemetria live, afisata separat de mesajul operatiei. |

`Awake()` verifica configuratia si citeste ID-ul salvat. `Update()` actualizeaza telemetria si starea butoanelor.

### Stari functionale usor de explicat

| Situatie | Ce poti face |
|---|---|
| Niciun cub, niciun ID | Plasezi un cub. |
| Cub nou, calitate insuficienta | Continui scanarea sau stergi cubul. |
| Cub nou urmarit, calitate acceptata | Poti apasa Host. |
| Host/Resolve in curs | Nu pornesti alta operatie si nu stergi cubul. |
| Host reusit, cub prezent | Poti sterge reprezentarea locala. |
| Niciun cub, ID salvat | Poti incerca Resolve sau plasa un cub nou. |
| Resolve reusit | Cubul reapare sub ancora rezolvata. |

Tabelul descrie comportamentul, nu un `enum` explicit din cod. Starea este derivata din referinte si proprietati.

## 6. Calitatea hartii - partea importanta pentru Host

### 6.1. `RefreshMappingStatus()`

Actualizeaza informatiile la aproximativ `0.25` secunde de timp real, imediat daca se schimba starea/motivul trackingului sau la o solicitare fortata.

La fiecare evaluare:

- actualizeaza mesajul despre sesiune si eventual `ARSession.notTrackingReason`;
- invalideaza vechea calitate prin `mappingQuality = null`;
- verifica daca evaluarea este permisa;
- daca este permisa, cere o noua estimare;
- recompune statusul.

De ce invalideaza valoarea? Un `Good` vechi nu trebuie sa ramana acceptat dupa pierderea trackingului.

### 6.2. `GetQualityUnavailableReason()`

Blocheaza apelurile native atunci cand, de exemplu:

- aplicatia ruleaza in Editor;
- ARCore Extensions sau Cloud Anchor Mode nu sunt active;
- sesiunea nu este in tracking;
- sesiunea sau subsistemul de ancore nu ruleaza;
- originea ori camera nu sunt pregatite.

In timpul unei operatii cloud, estimarea periodica este de asemenea suspendata. UI-ul explica indisponibilitatea, in loc sa prezinte o calitate veche drept actuala.

### 6.3. De ce transformam pozitia camerei

Apelul SDK cere un pose in **session space**, nu direct in world space:

```csharp
Pose cameraPose = extensions.Origin.TrackablesParent.InverseTransformPose(
    new Pose(arCamera.transform.position, arCamera.transform.rotation));

mappingQuality =
    arAnchorManager.EstimateFeatureMapQualityForHosting(cameraPose);
```

`InverseTransformPose` transforma pozitia si rotatia camerei din world in sistemul de coordonate al parintelui trackables.

Fara aceasta conversie, o translatie, rotatie, scara sau un offset al ierarhiei originii poate conduce la evaluarea unui pose nepotrivit. Coordonate numeric plauzibile nu inseamna automat coordonate in spatiul corect.

### 6.4. Pragul folosit

Sunt acceptate numai:

- `FeatureMapQuality.Sufficient`;
- `FeatureMapQuality.Good`.

`Insufficient` blocheaza cererea. Nu exista un al doilea click care sa forteze ignorarea pragului.

**Nu confunda:**

- multe plane cu o harta vizuala buna;
- rotirea pe loc cu observarea din pozitii diferite;
- o estimare acceptata cu garantia unui Host/Resolve reusit.

Pentru o scanare mai buna: lumina stabila, detalii vizuale, obiecte nemiscate si observarea aceleiasi zone din pozitii/unghiuri diferite. Daca tableta este legata prin cablu, foloseste spatiul de miscare disponibil; conexiunea la laptop nu imbunatateste estimarea mediului.

## 7. Host, pas cu pas

### 7.1. `Host()`

1. `CanUseCloud()` verifica disponibilitatea controllerului, lipsa unei alte operatii, configuratia/trackingul si lipsa unei stari explicit offline.
2. Verifica existenta unui cub nou si a ancorei pregatite.
3. Verifica trackingul ancorei.
4. Forteaza o evaluare proaspata a calitatii.
5. Refuza `Insufficient` sau o calitate indisponibila.
6. Porneste cererea:

```csharp
hostPromise = arAnchorManager.HostCloudAnchorAsync(pendingHostAnchor, 1);
operation = StartCoroutine(WaitHostingResult(hostPromise));
```

Argumentul `1` reprezinta durata in zile. Cu autentificarea prin cheia API folosita aici, durata de hosting este de o zi.

`Application.internetReachability` detecteaza o situatie offline, dar nu garanteaza ca serviciul Google este accesibil sau autorizat. Erorile reale ale cererii trebuie in continuare tratate.

### 7.2. `WaitHostingResult()`

```csharp
yield return promise;
```

Nu blocheaza bucla principala pana vine raspunsul. Coroutine-ul continua dupa finalizarea operatiei.

Apoi:

- anuleaza starea ocupata locala;
- trateaza anularea;
- verifica `CloudAnchorState.Success`;
- verifica si existenta unui ID nevid;
- la succes apeleaza `SaveAnchorId`;
- la esec apeleaza `ReportFailure`.

### 7.3. `SaveAnchorId()`

Salveaza ID-ul in `PlayerPrefs`, sub cheia logica `IRVA.L3.CloudAnchorId`, si cheama `PlayerPrefs.Save()`.

Consecinte:

- ID-ul supravietuieste inchiderii si redeschiderii aplicatiei.
- Un Host nou, reusit, inlocuieste ID-ul anterior.
- Un Host esuat nu inlocuieste ID-ul salvat cu o valoare invalida.
- Stergerea datelor aplicatiei/dezinstalarea poate elimina aceasta preferinta.
- Este persistenta locala pe dispozitiv, nu un mecanism de distribuire a ID-ului catre alte dispozitive.

Cubul ramane vizibil dupa Host. Doar referinta `pendingHostAnchor` este golita, astfel incat acel cub sa nu fie publicat repetat ca un cub nou.

## 8. Resolve, pas cu pas

### 8.1. `Resolve()`

Verifica:

- ca operatiile cloud pot fi folosite;
- ca exista un ID;
- ca nu exista deja un cub local.

De aceea demonstratia foloseste ordinea **Host -> Delete -> Resolve**.

```csharp
resolveDeadline = Time.realtimeSinceStartupAsDouble + ResolveTimeoutSeconds;
resolvePromise = arAnchorManager.ResolveCloudAnchorAsync(anchorIdToResolve);
```

### 8.2. `WaitResolvingResult()`

Incepe cu `yield return null`, astfel incat handle-ul coroutine-ului sa poata fi atribuit chiar daca promisiunea este deja finalizata foarte rapid.

Cat timp promisiunea este `Pending`:

- verifica termenul limita;
- afiseaza periodic timpul ramas si ghidajul de scanare;
- cedeaza executia pana la urmatorul cadru.

La succes, verifica `result.Anchor` si apeleaza:

```csharp
placement.RecreatePlacement(result.Anchor.transform);
```

Nu reutilizeaza pozitia world memorata a cubului sters. Se bazeaza pe ancora returnata de relocalizare.

### 8.3. Timeout-ul de 60 de secunde

- Foloseste timp real, independent de `Time.timeScale`.
- Anuleaza incercarea inca pendinta.
- Elibereaza starea ocupata si reactiveaza controalele potrivite.
- Pastreaza ID-ul pentru reincercare.
- Daca finalizarea castiga cursa cu anularea, rezultatul finalizat este tratat normal.

**60 de secunde nu este durata de viata a ancorei.** Este limita locala a unei incercari de Resolve. TTL-ul este o zi de la Host reusit, nu pana la miezul noptii.

Timeout-ul nu dovedeste ca ancora a expirat. Poate insemna ca mediul nu a fost recunoscut, trackingul a fost slab sau comunicarea a avut probleme.

Timeout-ul local de 60 s este implementat pentru Resolve, nu si pentru Host.

## 9. UI, erori si curatare

### Butoanele

`RefreshButtons()` aplica regulile principale:

- Host: cub nou, ancora urmarita, calitate acceptata, configuratie valida, nicio operatie in curs.
- Resolve: ID disponibil, niciun cub local, nicio operatie in curs.
- Delete: cub local, nicio operatie in curs.

Metodele actiunilor fac si ele verificari. Un buton activ nu este singura protectie impotriva unei operatii invalide.

### Statusul

`RenderStatus()` combina:

```text
starea trackingului
calitatea pentru Host

mesajul ultimei actiuni sau al operatiei curente
```

Actualizarea telemetriei nu trebuie sa stearga imediat o eroare, confirmarea unui Host sau mesajul de timeout. `ShowOperationStatus()` afiseaza timpul ramas si recomandarile pentru Resolve.

### Erorile SDK

`ReportFailure()` traduce cateva stari in recomandari:

| Stare | Ce explici/verifici |
|---|---|
| `ErrorNotAuthorized` | Autorizarea serviciului si configuratia existenta. |
| `ErrorResolvingCloudIdNotFound` | ID neregasit; ancora poate fi expirata. |
| `ErrorHostingServiceUnavailable` | Serviciul/conectivitatea; incearca din nou in conditii bune. |
| `ErrorHostingDatasetProcessingFailed` | Datele scanarii nu au putut fi procesate; trebuie o scanare mai buna. |
| `ErrorResourceExhausted` | Limitele de utilizare ale proiectului. |

Logurile consemneaza calitatea si starea operatiei, nu cheia API sau ID-ul ancorei.

### La dezactivare sau parasirea scenei

`OnDisable()` din manager:

- opreste coroutine-ul;
- cere anularea promisiunilor;
- trateaza si o eventuala finalizare concurenta;
- pastreaza un ID valid de Host daca operatia tocmai reusise;
- elimina o ancora de Resolve finalizata care nu mai trebuie folosita;
- dezactiveaza butoanele.

Reprezentarea locala este eliberata de scriptul de plasare in `OnDestroy`. Dezactivarea doar a managerului cloud nu este identica cu apasarea Delete.

## 10. Demonstratia recomandata

1. Verifici internetul si alegi o zona bine luminata, cu detalii nemiscate.
2. Scanezi pana ai tracking si un plan.
3. Plasezi un singur cub.
4. Observi zona din mai multe pozitii pana calitatea este `Sufficient` sau `Good`.
5. Apesi Host si astepti confirmarea explicita de succes.
6. Explici ca ID-ul a fost salvat, dar cubul vizibil este inca reprezentarea locala.
7. Apesi Delete: cubul dispare, ID-ul ramane.
8. Apesi Resolve si scanezi aceeasi zona.
9. Arati cubul recreat la ancora regasita.

Optional, demonstrezi persistenta redeschizand aplicatia in intervalul de valabilitate si rezolvand ID-ul salvat, fara sa stergi datele aplicatiei.

Pregateste un Host recent pentru prezentare. Nu conta pe o ancora creata cu mai mult de o zi inainte.

## 11. Intrebari posibile

**De ce nu salvezi doar pozitia cubului?**  
Pentru ca originea spatiala a unei sesiuni noi nu identifica automat acelasi loc fizic. Cloud Anchors permite relocalizarea.

**Ce sterge Delete?**  
Doar ancora si cubul local. ID-ul si ancora gazduita raman disponibile in limitele serviciului si ale TTL-ului.

**Se incarca prefab-ul in cloud?**  
Nu. Aplicatia il instantiaza local dupa regasirea ancorei.

**De ce esti obligata sa scanezi si la Resolve?**  
Un ID identifica ancora, dar camera trebuie sa observe mediul pentru a o localiza spatial.

**De ce `Good` nu garanteaza succesul?**  
Este o estimare locala a calitatii. Autorizarea, comunicarea, procesarea si relocalizarea au propriile conditii de succes.

**Este implementata partajarea intre dispozitive?**  
Serviciul poate sustine astfel de scenarii, dar aplicatia aceasta pastreaza un singur ID local si nu implementeaza transferul lui sau bonusul cu mai multe ancore.

**Ce se intampla dupa timeout?**  
Se opreste incercarea, nu se sterge ID-ul. Pot reface scanarea si reincerca.

## 12. Varianta de prezentare in aproximativ un minut

> Aplicatia plaseaza un cub pe o ancora locala si il publica prin Cloud Anchors. Scriptul de plasare gestioneaza atingerea si cubul, iar managerul cloud gestioneaza calitatea, promisiunile asincrone, ID-ul si UI-ul. Host este permis numai la Sufficient sau Good, cu o estimare proaspata bazata pe pose-ul camerei convertit in session space. La succes salvez ID-ul in PlayerPrefs pentru o ancora cu durata de o zi. Delete elimina doar reprezentarea locala. Resolve foloseste ID-ul si scanarea aceleiasi zone pentru relocalizare, apoi recreeaza prefab-ul sub ancora returnata. Dupa 60 de secunde opresc o incercare de Resolve inca pendinta si pastrez ID-ul pentru retry. Implementarea este pentru o singura ancora, fara bonusul multiplu.

### Checklist

- [ ] Diferentiez ancora locala, Cloud Anchor si continutul vizual.
- [ ] Pot explica responsabilitatile celor doua scripturi.
- [ ] Inteleg session space si de ce se converteste pose-ul camerei.
- [ ] Stiu de ce Host cere o estimare noua si accepta doar Sufficient/Good.
- [ ] Inteleg promise, coroutine si interdictia operatiilor simultane.
- [ ] Pot explica exact ce ramane dupa Delete.
- [ ] Diferentiez persistenta ID-ului, TTL-ul de o zi si timeout-ul de 60 s.
- [ ] Stiu cum este recreat cubul dupa Resolve si cum sunt tratate esecurile.

**Navigare:** [Laboratorul 2](Laboratorul_2.md) | [Laboratorul 4](Laboratorul_4.md).
