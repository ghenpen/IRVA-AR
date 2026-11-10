# Laboratorul 4 - Augmented Images, Augmented Faces si bonusul mastilor

Laboratorul are doua parti, in doua scene. Acest ghid le acopera pe amandoua.

| Parte | Implementarea actuala | Scena |
|---|---|---|
| Cerintele 1-3 | Biblioteca initiala cu trei imagini, augmentare si biblioteca runtime cu o imagine descarcata. | [L4_AugmentedImages.unity](../Assets/Scenes/L4_AugmentedImages.unity) |
| Cerinta 4 | Detectia fetei si masca albastra semitransparenta. | [L4_AugmentedFaces.unity](../Assets/Scenes/L4_AugmentedFaces.unity) |
| Bonus | Trei accesorii 3D: ochelari, urechi de pisica, coroana; selectare directa si ciclica. | Aceeasi scena de fete. |

**Sunt patru optiuni la fete:** albastra, ochelari, pisica, coroana. Albastra este optiunea implicita, nu a fost inlocuita de bonus.

## 1. Diferenta dintre cele doua tipuri de tracking

| Aspect | Imagini | Fete |
|---|---|---|
| Ce urmarim | O tinta 2D cunoscuta dintr-o biblioteca. | Geometria si pozitia unei fete. |
| Camera folosita | Din spate, `World`. | Frontala, `User`. |
| Manager | `ARTrackedImageManager`. | `ARFaceManager`. |
| Continut | Cub copil al imaginii urmarite. | Mesh-ul fetei si/sau un accesoriu copil al fetei. |
| Date suplimentare | Biblioteca de referinta si starea imaginii. | Mesh facial; pentru bonus, reperele faciale ARCore. |
| Internet in fluxul aplicatiei | Pentru prima descarcare a imaginii runtime. | Nu exista descarcare sau apel Cloud Anchors. |
| Cod propriu | [ARRuntimeImageLibrary.cs](../Assets/Scripts/ARRuntimeImageLibrary.cs) | [ARFaceMaskController.cs](../Assets/Scripts/ARFaceMaskController.cs) |

In niciuna dintre parti nu ai implementat de la zero algoritmul de recunoastere/tracking. Codul propriu gestioneaza datele SDK-ului, continutul, selectia, transformarile si erorile.

---

## PARTEA A - AUGMENTED IMAGES

## 2. Biblioteca initiala si tinta runtime

Biblioteca [Lab4ReferenceImageLibrary.asset](../Assets/Lab4ReferenceImageLibrary.asset) contine exact trei tinte:

| Tinta de aratat camerei | Biblioteca |
|---|---|
| [Earth](../Assets/Textures/Lab4/Earth.jpg) | Initiala |
| [Astronaut](../Assets/Textures/Lab4/Astronaut.jpg) | Initiala |
| [LunarRover](../Assets/Textures/Lab4/LunarRover.jpg) | Initiala |
| [Footprint-Runtime](../Assets/Textures/Lab4/Footprint-Runtime.jpg) | Runtime, separata de primele trei |

Fotografiile sunt imagini NASA; sursele si creditarea sunt in [README](../../README.md).

Fisierul local al tintei Footprint este util pentru afisare pe laptop sau tiparire. **Codul runtime nu il incarca din Assets:** descarca imaginea de la URL-ul NASA definit in script.

### Ce inseamna biblioteca de referinta

Este colectia tintelor pe care providerul trebuie sa le recunoasca. Nu reprezinta lista obiectelor deja detectate.

- `XRReferenceImageLibrary`: asset-ul configurat in Editor.
- `RuntimeReferenceImageLibrary`: forma bibliotecii folosita de provider la rulare.
- `MutableRuntimeReferenceImageLibrary`: biblioteca runtime la care se pot adauga imagini, daca providerul suporta aceasta functionalitate.

Pentru fiecare imagine recunoscuta, managerul ofera un `ARTrackedImage`: transformare, identificator, referinta la tinta si stare de tracking.

### Cum alegi si prezinti o tinta buna

Detaliile distincte, contrastul si lipsa repetitiilor excesive ajuta recunoasterea. O imagine uniforma sau cu un model repetitiv este o tinta slaba.

Imaginea trebuie sa fie suficient de mare in cadru, clara, plana si fara reflexii puternice. Arata tinta intreaga, nu doar o decupare.

Dimensiunile fizice nu sunt fixate in aceste biblioteci; providerul estimeaza dimensiunea. Rezolutia in pixeli si latimea fizica in metri sunt lucruri diferite. In cod, adaugarea runtime foloseste `null` pentru latimea fizica.

## 3. Cum este organizat scriptul de imagini

Deschide [ARRuntimeImageLibrary.cs](../Assets/Scripts/ARRuntimeImageLibrary.cs).

| Camp | Rol |
|---|---|
| `m_PlacedPrefab` | Prefab-ul cubului de augmentare. |
| `initialLibrary` | Asset-ul cu cele trei tinte initiale. |
| `runtimeLibrary` | Biblioteca runtime reusita, pastrata pentru reutilizare. |
| `content` | Dictionar `TrackableId -> GameObject`, cate o augmentare proprie pentru fiecare imagine urmarita. |
| `activeImageIds` | Set cu GUID-urile tintelor permise in biblioteca activa. |
| `downloadRequest` | Cererea de descarcare in curs. |
| `downloadedTexture` | Textura temporara descarcata. |
| `addImageJob` / `jobPending` | Starea operatiei de validare si adaugare a imaginii. |
| `busy` | Blocheaza cereri repetate in timpul pregatirii bibliotecii. |
| `usingRuntimeLibrary` | Selectia logica actuala, folosita si pentru UI. |

**Doua identificatoare diferite:**

- `referenceImage.guid` identifica tinta din biblioteca.
- `trackableId` identifica instanta urmarita de manager.

Nu trebuie folosit numele afisat al imaginii drept identificator unic al fiecarui obiect urmarit.

### Initializare si evenimente

- `Awake()` verifica referintele si existenta a cel putin trei imagini initiale, memoreaza GUID-urile si initializeaza UI-ul.
- `OnEnable()` se aboneaza la `trackablesChanged` si reconciliaza imediat imaginile deja cunoscute de manager.
- `OnTrackedImagesChanged()` proceseaza adaugari, actualizari si eliminari.
- `OnDisable()` elimina abonarea, opreste incarcarea, elibereaza resursele temporare si elimina continutul propriu.

Abonarea si dezabonarea simetrice evita callback-uri ramase pe un controller inactiv.

## 4. Cum apare un cub pe o imagine

```text
ARCore recunoaste o tinta din biblioteca
    -> ARTrackedImageManager actualizeaza ARTrackedImage
    -> trackablesChanged
    -> UpdateContent
    -> verifica biblioteca activa si TrackingState
    -> creeaza/reutilizeaza cubul ca fiu al imaginii
```

Conditia din `UpdateContent()`:

```csharp
bool visible = activeImageIds.Contains(image.referenceImage.guid) &&
    image.trackingState == TrackingState.Tracking;
```

- Apartenenta la biblioteca activa este verificata prin `HashSet<Guid>`.
- Este cerut `Tracking`, nu doar existenta unui obiect de tip imagine.
- Daca exista deja un cub in dictionar, se actualizeaza vizibilitatea lui.
- Daca nu exista si tinta este vizibila, se instantiaza prefab-ul.

```csharp
instance = Instantiate(m_PlacedPrefab, image.transform, false);
instance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
```

Fiind copilul imaginii, cubul urmeaza pozitia si rotatia acesteia. Pozitia locala zero nu inseamna pozitia world `(0, 0, 0)`.

`Tracked Image Prefab` ramane `None`: managerul gestioneaza trackable-ul, iar scriptul gestioneaza separat augmentarea. Nu se dubleaza continutul prin doua mecanisme.

### Pierderea tintei

- Pentru `Limited` sau `None`, cubul este ascuns, nu lasat la o pozitie veche ca si cum trackingul ar fi sigur.
- Daca tinta revine si instanta exista, cubul este reutilizat.
- La eliminarea trackable-ului, cubul propriu este distrus si intrarea este scoasa din dictionar.

## 5. Biblioteca runtime, pas cu pas

### 5.1. `UseRuntimeLibrary()`

1. `CanChangeLibrary()` verifica configuratia, starea ocupata, rularea pe dispozitiv si trackingul.
2. Daca biblioteca runtime exista deja, o reactiveaza.
3. Daca nu exista, verifica `supportsMutableLibrary`.
4. Verifica lipsa unei stari explicit offline.
5. Seteaza `busy` si porneste coroutine-ul `CreateRuntimeLibrary()`.

Biblioteca initiala ramane activa in timpul descarcarii si validarii. Nu este eliminata preventiv.

### 5.2. Descarcarea

```csharp
downloadRequest = UnityWebRequestTexture.GetTexture(RuntimeImageUrl, false);
downloadRequest.timeout = 30;
yield return downloadRequest.SendWebRequest();
```

- Descarcarea se face de la URL-ul NASA din script.
- Timeout-ul cererii este de 30 de secunde.
- `false` pentru parametrul `nonReadable` pastreaza accesul la pixelii necesari pregatirii imaginii.
- `yield` permite continuarea aplicatiei in timp ce cererea este in curs.

La rezultat se verifica succesul, existenta texturii si o rezolutie de cel putin `300 x 300` pixeli. Se reverifica si trackingul dupa descarcare.

### 5.3. `TryScheduleImage()` - biblioteca mutabila si job-ul

```csharp
candidate = trackImageManager.CreateRuntimeLibrary()
    as MutableRuntimeReferenceImageLibrary;

addImageJob = candidate.ScheduleAddImageWithValidationJob(
    texture, RuntimeImageName, null);
```

`candidate` este o biblioteca noua, nu biblioteca initiala cu o imagine adaugata.

Operatia SDK pregateste si valideaza tinta. Scriptul:

1. asteapta ca `jobHandle.IsCompleted` sa devina adevarat;
2. cheama `Complete()` pentru finalizarea sincronizarii job-ului;
3. verifica `AddReferenceImageJobStatus.Success`;
4. verifica faptul ca noua biblioteca are exact o imagine.

Abia apoi biblioteca este retinuta si activata.

In versiunea SDK folosita, overload-ul cu textura copiaza pixelii la programarea job-ului. De aceea textura descarcata poate fi distrusa dupa programare, inaintea activarii finale.

**Coroutine-ul si job-ul nu sunt acelasi lucru:** coroutine-ul coordoneaza asteptarea intre cadre, iar job-ul este operatia SDK de procesare/adaugare.

### 5.4. Erori si resurse temporare

- Erorile de descarcare, suport sau validare apar in status.
- Exceptiile asteptate ale API-ului sunt tratate explicit.
- O pregatire esuata nu selecteaza o biblioteca incompleta.
- Blocul `finally` elibereaza cererea/textura/job-ul ramas si reseteaza starea ocupata.
- `ReleaseLoadingResources()` poate anula si elibera cererea si finalizeaza un job inca pendinte inainte de curatare.

Nu se raporteaza succes doar pentru ca descarcarea HTTP a reusit; si adaugarea in biblioteca trebuie sa reuseasca.

## 6. Comutarea bibliotecilor si problema rezolvata

`UseInitialLibrary()` si `UseRuntimeLibrary()` ajung la `TryActivateLibrary()`.

Aceasta:

1. retine biblioteca si starea managerului anterior;
2. atribuie noua biblioteca nenula, fara oprirea prealabila a managerului;
3. cere suport pentru pana la trei imagini in miscare;
4. la o eroare de activare, restaureaza configuratia precedenta;
5. elimina augmentarile vechi;
6. memoreaza GUID-urile bibliotecii active;
7. parcurge imediat trackables existente;
8. actualizeaza UI-ul.

Valoarea ceruta pentru imaginile in miscare nu garanteaza ca orice dispozitiv poate urmari simultan acel numar.

### De ce este obligatoriu `RefreshTrackedContent()`?

Secventa problematica era:

```text
Runtime -> Initiala -> Runtime
```

La revenire, managerul poate cunoaste deja tinta Footprint. Nu este garantat ca va emite imediat un nou eveniment `added` sau `updated`.

Daca aplicatia a distrus cubul si asteapta numai evenimente viitoare, poate ramane fara augmentare desi trackable-ul exista.

Solutia:

```csharp
foreach (ARTrackedImage image in trackImageManager.trackables)
    UpdateContent(image);
```

**Ideea generala:** dupa schimbarea configuratiei, reconciliezi starea curenta a managerului, nu te bazezi exclusiv pe evenimente viitoare.

Este folosita aceeasi reconciliere si la reactivarea controllerului.

### Cache si durata de viata

Biblioteca runtime reusita este pastrata in campul `runtimeLibrary`. Comutarile ulterioare in aceeasi instanta a scenei nu redescarca imaginea.

Dezactivarea temporara a controllerului nu goleste acel camp. Reincarcarea scenei creeaza insa o instanta noua, fara acest cache.

## 7. Demonstratia pentru imagini

1. Pregatesti pe laptop sau tiparesti tintele din tabel.
2. Pornesti scena cu biblioteca initiala.
3. Arati Earth, Astronaut si LunarRover, pe rand, si cuburile atasate.
4. Indepartezi o tinta din cadru: augmentarea nu trebuie sa ramana afisata drept urmarita.
5. Cu internet disponibil, apesi Runtime si astepti succesul descarcarii/validarii.
6. Arati Footprint si cubul corespunzator.
7. Revii la Initiala si apoi din nou la Runtime.
8. Explici de ce a doua activare foloseste cache-ul si reconciliaza trackables existente.

Biblioteca runtime nu este biblioteca initiala plus Footprint: sunt doua seturi separate.

---

## PARTEA B - AUGMENTED FACES SI BONUS

## 8. Configuratia scenei de fete

- `ARCameraManager` cere `CameraFacingDirection.User`.
- `ARSession` cere `TrackingMode.RotationOnly`.
- `ARFaceManager` gestioneaza detectia fetei; scena cere o fata.
- Managerii pentru plane/puncte si extensia cloud nefolosita nu sunt activi in aceasta scena.
- Componenta de imagini nu este folosita pentru fete.

Imaginile si fetele sunt separate deoarece au configuratii si fluxuri diferite, in special pentru camera.

Prefab-ul folosit de manager este [Tracked Face.prefab](../Assets/Prefabs/FaceAccessories/Tracked%20Face.prefab), cu:

| Componenta | Rol |
|---|---|
| `ARFace` | Datele si transformarea fetei urmarite. |
| `ARFaceMeshVisualizer` | Actualizeaza geometria vizibila conform datelor SDK. |
| `MeshFilter` | Referinta la mesh. |
| `MeshRenderer` | Deseneaza mesh-ul cu materialul selectat. |

Detectia si deformarea mesh-ului sunt responsabilitatea SDK-ului. Nu este necesar un `MeshCollider` pentru efectele implementate aici.

## 9. Cerinta de baza: masca albastra

Materialul original este [Lab4 Face Mask.mat](../Assets/Prefabs/Lab4%20Face%20Mask.mat), un material URP Unlit turcoaz, semitransparent, cu alpha `0.5`.

Este aplicat mesh-ului facial, care urmeaza forma fetei prin `ARFaceMeshVisualizer`.

- Nu este un dreptunghi albastru lipit de ecran.
- Nu este un accesoriu 3D separat generat la pozitia nasului.
- Nu depinde de obtinerea reperelor speciale ARCore pentru nas si frunte.
- Unlit inseamna ca aspectul nu este calculat ca pentru un material luminat de luminile scenei.

Prefab-ul original [Lab4 Face Mask.prefab](../Assets/Prefabs/Lab4%20Face%20Mask.prefab) a fost pastrat. Prefab-ul folosit pentru bonus porneste tot cu materialul albastru.

## 10. Selectia celor patru moduri

Deschide [ARFaceMaskController.cs](../Assets/Scripts/ARFaceMaskController.cs).

| Index `selectedMask` | Mod | Material pe mesh-ul fetei | Prefab accesoriu |
|---|---|---|---|
| 0 | Albastra | Albastru | Niciunul |
| 1 | Ochelari | Depth-only | [Ochelari.prefab](../Assets/Prefabs/FaceAccessories/Ochelari.prefab) |
| 2 | Pisica | Depth-only | [Pisica.prefab](../Assets/Prefabs/FaceAccessories/Pisica.prefab) |
| 3 | Coroana | Depth-only | [Coroana.prefab](../Assets/Prefabs/FaceAccessories/Coroana.prefab) |

**Patru butoane, dar numai trei prefab-uri de accesorii.** Pentru modurile 1-3, prefab-ul este accesat prin `maskPrefabs[selectedMask - 1]`.

`selectedMask` porneste de la zero. Alegerea se pastreaza in controller cat timp ramane aceeasi instanta a scenei, inclusiv daca fata dispare si reapare. Nu este salvata in `PlayerPrefs`.

### Metodele pentru selectie

- `Awake()`: verifica originea, managerul, cele trei prefab-uri, cele doua materiale, patru selectoare, Next si statusul.
- `SelectMask(index)`: verifica indexul 0-3 si starea controllerului; la schimbare elimina accesoriul vechi, apoi actualizeaza fata si UI-ul.
- `NextMask()`: calculeaza `(selectedMask + 1) % 4`.
- `RefreshButtons()`: dezactiveaza butonul modului deja selectat si controleaza disponibilitatea celorlalte.

Operatorul modulo face ciclul:

```text
Albastra -> Ochelari -> Pisica -> Coroana -> Albastra
```

La revenirea la albastra, accesoriul este eliminat si materialul original este reaplicat. Nu ramane obligatoriu o masca albastra sub ochelari sau coroana.

## 11. Trackingul selfie: diferenta critica fata de laboratoarele anterioare

Camera frontala ARCore nu ofera trackingul mediului in acelasi mod ca cea din spate. [Documentatia ARCore pentru camera frontala](https://developers.google.com/ar/reference/java/com/google/ar/core/CameraConfig.FacingDirection#FRONT) precizeaza ca trackingul camerei raporteaza `PAUSED`.

Prin urmare, sesiunea poate ramane `SessionInitializing` chiar daca o fata individuala are `TrackingState.Tracking`.

Codul accepta:

```csharp
bool sessionRunning =
    ARSession.state == ARSessionState.SessionInitializing ||
    ARSession.state == ARSessionState.SessionTracking;
```

Apoi verifica separat:

1. managerul de fete este activ;
2. subsistemul exista si ruleaza;
3. pentru accesorii, exista subsistemul ARCore cu reperele necesare;
4. fiecare fata este urmarita corespunzator.

**Nu este ocolita verificarea trackingului.** Este verificat trackingul potrivit: cel al fetei, nu o conditie de tracking al mediului care nu este disponibila in modul selfie.

Aceasta era cauza logica a blocajului in care accesoriile asteptau permanent camera. Conditia potrivita pentru camera din spate din laboratoarele 2-3 nu trebuie copiata automat aici.

## 12. Ce face `UpdateFaces()`

Metoda este apelata la activare, la selectie si in `LateUpdate`.

```text
aplica materialul modului ales
    -> ascunde preventiv accesoriile proprii
    -> verifica platforma, sesiunea si subsistemul
    -> pentru fiecare fata:
         albastra: lasa vizualizarea mesh-ului in seama SDK-ului
         accesoriu: cere Tracking, obtine reperele, calculeaza pozitia/scara
                    creeaza sau reutilizeaza instanta
                    actualizeaza transformarea locala si o afiseaza
    -> actualizeaza statusul
```

- Albastra foloseste vizualizatorul standard; contorul accepta starile fetei diferite de `None`.
- Accesoriile necesita explicit `TrackingState.Tracking`.
- `Limited`, `None` sau repere invalide lasa accesoriile ascunse.
- Ascunderea preventiva evita folosirea unei pozitii vechi cand datele actuale lipsesc.
- In Editor, scriptul afiseaza instructiunea de testare pe tableta, fara interogarea nativa a reperelor.

Contorul `Fete urmarite` din HUD este calculat pentru modul afisat. La accesorii, zero poate insemna si ca nu exista repere valide pentru afisare, nu doar ca detectorul nu a observat nicio fata.

## 13. Reperele fetei si calculul pozitiei

### 13.1. Cele trei repere

```csharp
subsystem.GetRegionPoses(face.trackableId, Allocator.Persistent, ref regions);
```

Se folosesc:

- `NoseTip`: varful nasului;
- `ForeheadLeft`: reperul stang al fruntii;
- `ForeheadRight`: reperul drept al fruntii.

Nu este eye tracking si nu este identificarea persoanei. Sunt date geometrice pentru pozitionarea continutului.

### 13.2. Conversia corecta a coordonatelor

Reperele sunt in session space. Accesoriul va fi copil al fetei, deci are nevoie de pozitii locale fata de fata.

```csharp
Vector3 local = face.transform.InverseTransformPoint(
    origin.TrackablesParent.TransformPoint(region.pose.position));
```

Citeste expresia din interior spre exterior:

1. `TransformPoint`: session space -> Unity world, prin parintele trackables.
2. `InverseTransformPoint`: Unity world -> coordonate locale ale fetei.

Asignarea directa a unei pozitii din session space in `localPosition` ar amesteca doua sisteme de coordonate. Ar putea parea corecta intr-o configuratie simpla, dar ar esua la offset-uri, rotatii sau scari diferite ale originii.

### 13.3. Scara si centrul fruntii

```csharp
scale = Vector3.Distance(left, right);
Vector3 forehead = (left + right) * 0.5f;
```

Distanta dintre reperele fruntii este folosita ca factor de scara pentru modelele normalizate. Este o masura relativa pentru potrivirea accesoriului, nu o masuratoare anatomica certificata.

`TryGetLayout()` refuza:

- lipsa oricaruia dintre cele trei repere;
- coordonate `NaN` sau infinite;
- o scara invalida sau prea mica, `<= 0.001f`.

`IsFinite()` este verificarea componentelor vectorului. Aceste verificari previn transformari absurde cand datele trackingului sunt incomplete.

### 13.4. Formulele accesoriilor

In tabel, `s` este `scale`, iar directiile sunt locale fetei:

| Accesoriu | Pozitia folosita |
|---|---|
| Ochelari | `Lerp(nose, forehead, 0.45) + back * (s * 0.12)` |
| Pisica | `forehead + up * (s * 0.4) + forward * (s * 0.1)` |
| Coroana | `forehead + up * (s * 0.35) + forward * (s * 0.5)` |

`Lerp(a, b, t)` interpoleaza intre doua pozitii; la `0.45` suntem la 45% din drumul de la nas spre centrul fruntii.

Coeficientii reprezinta reglaje vizuale pentru modelele stilizate, nu constante impuse de SDK. Important este sa poti explica reperul, offset-ul si proportionalitatea, nu sa memorezi toate zecimalele.

Instanta primeste:

```csharp
instance.transform.SetLocalPositionAndRotation(position, Quaternion.identity);
instance.transform.localScale = Vector3.one * scale;
```

Rotatia locala identitate inseamna ca orientarea este mostenita de la parintele `ARFace`. Scriptul foloseste pozitiile reperelor pentru layout, nu aplica separat rotatia fiecarui region pose.

## 14. De ce accesoriile nu sunt create in fiecare cadru

`content` este un dictionar `TrackableId -> GameObject`.

- La prima afisare a modului pentru fata, se instantiaza prefab-ul.
- In cadrele urmatoare se actualizeaza aceeasi instanta.
- La schimbarea modului se sterge continutul vechi.
- La eliminarea fetei, `OnFacesChanged()` elimina intrarea asociata.
- Daca apare o fata noua, se aplica selectia curenta.

Modelele sunt asset-uri existente, create local. Scriptul runtime nu reconstruieste procedural geometria ochelarilor sau coroanei in fiecare cadru.

## 15. Ocluzia: de ce fata reala poate ascunde accesorii virtuale

Pentru modurile cu accesorii, mesh-ul fetei foloseste [FaceOcclusion.shader](../Assets/Prefabs/FaceAccessories/FaceOcclusion.shader).

Problema: imaginea camerei este un fundal. Singura, ea nu furnizeaza automat un obiect Unity care sa ascunda bratul ochelarilor aflat in spatele fetei.

Solutia: mesh-ul urmarit al fetei scrie **adancime, fara culoare**, aproximand geometria care trebuie sa ocluda accesoriile.

| Setare shader | Rol |
|---|---|
| `Queue = Geometry-1` | Deseneaza aceasta geometrie inaintea geometriei opace obisnuite. |
| `ColorMask 0` | Nu scrie culoare in imagine. |
| `ZWrite On` | Scrie in bufferul de adancime. |
| `ZTest LEqual` | Foloseste testul de adancime pentru fragmente. |
| `Cull Off` | Nu elimina automat una dintre orientarile fetelor triunghiurilor. |
| `TransformObjectToHClip` | Transforma vertecsii din spatiul obiectului in spatiul necesar randarii. |

Astfel fata reala ramane vizibila in fundal, dar partile accesoriului aflate in spatele mesh-ului pot fi ascunse de testul de adancime.

**Nu este suficient sa faci doar materialul complet transparent.** Ocluzia necesita comportamentul corect al scrierii/testarii adancimii.

Limita: ocluzia aproximeaza fata prin mesh-ul urmarit; nu reconstruieste toate obiectele din jur, parul sau o mana trecuta prin fata.

## 16. Materiale si eliberarea resurselor

`ApplyFaceMaterial()` schimba `sharedMaterial` al rendererului fetei:

- albastru pentru modul de baza;
- depth-only pentru cele trei accesorii.

Schimba referinta materialului folosit de renderer, nu modifica proprietatile asset-ului albastru.

`OnDisable()`:

1. elimina abonarea la evenimente;
2. restaureaza albastrul pe fetele existente;
3. elimina accesoriile proprii;
4. elibereaza `NativeArray` pentru repere prin `Dispose`;
5. actualizeaza butoanele.

`NativeArray` reprezinta memorie nativa. Bufferul cu `Allocator.Persistent` poate fi reutilizat intre cadre, dar trebuie eliberat explicit; nu este suficient sa te bazezi pe garbage collector-ul C#.

Nu sunt distruse materialele si prefab-urile partajate ale proiectului, ci instantele create de controller.

## 17. Demonstratia pentru fete

1. Deschizi scena de fete si verifici camera frontala.
2. Iti tii fata bine luminata in cadru: apare albastra implicit.
3. Rotesti usor capul si arati ca mesh-ul urmeaza fata.
4. Alegi Ochelari: albastrul este inlocuit de materialul invizibil de ocluzie, iar accesoriul apare.
5. Alegi Pisica si Coroana.
6. Folosesti Urmatoarea si arati revenirea de la Coroana la Albastra.
7. Iesi temporar din cadru si revii: selectia trebuie sa ramana in aceeasi vizita a scenei.
8. Arati o rotire usoara pentru a explica ocluzia si urmarirea accesoriului.

Nu ai nevoie de imagini de referinta, descarcare runtime sau Cloud Anchors pentru aceasta parte.

## 18. Intrebari posibile

**De ce exista o biblioteca de imagini?**  
Providerul trebuie sa stie ce tinte sa recunoasca. Trackables sunt observatiile concrete ale acelor tinte, nu biblioteca insasi.

**Footprint este adaugata peste cele trei imagini?**  
Nu. Este intr-o biblioteca runtime separata, iar UI-ul comuta intre cele doua.

**De ce nu creezi cubul la fiecare eveniment de actualizare?**  
Ar produce duplicate. Dictionarul asociaza trackable-ul cu instanta deja creata.

**De ce mai parcurgi trackables dupa schimbarea bibliotecii?**  
Pentru ca o tinta deja cunoscuta poate sa nu emita imediat un eveniment nou. Reconcilierea reface continutul din starea existenta.

**Masca albastra si coroana folosesc acelasi mecanism vizual?**  
Nu complet. Albastra coloreaza mesh-ul dinamic al fetei. Coroana este un prefab 3D separat, pozitionat/scalat din repere.

**De ce nu ceri `SessionTracking` pentru selfie?**  
Trackingul camerei frontale poate ramane PAUSED, desi fata este urmarita. Verific sesiunea pornita, subsistemul activ si trackingul fetei.

**Cum urmeaza ochelarii rotatia capului?**  
Sunt copii ai `ARFace`, cu rotatie locala identitate. Mostenesc transformarea fetei.

**De ce convertesti reperele prin doua transformari?**  
Datele sunt in session space, iar accesoriul are nevoie de face-local space. Trec prin world pentru a respecta intreaga ierarhie.

**Cum ascunzi ochelarii din spatele fetei fara sa colorezi fata?**  
Mesh-ul facial scrie doar adancime, iar accesoriile sunt supuse testului de adancime.

**Se identifica persoana sau se urmareste privirea?**  
Nu. Implementarea foloseste geometria fetei si trei regiuni pentru pozitionarea accesoriilor.

## 19. Probleme de recunoscut

| Simptom | Ce verifici |
|---|---|
| Nu este recunoscuta imaginea | Biblioteca selectata, tinta completa, dimensiunea in cadru, lumina/reflexii, trackingul. |
| Runtime nu se creeaza | Internetul, URL-ul, suportul bibliotecii mutabile si mesajul validarii. |
| La a doua activare Runtime lipseste cubul | Reconcilierea trackables existente; nu presupune automat ca trebuie redescarcata imaginea. |
| Accesoriile asteapta camera permanent | Diferenta SessionInitializing/SessionTracking in modul selfie si starea subsistemului de fete. |
| Albastra merge, accesoriile nu | Suportul ARCore pentru repere, trackingul fetei si validitatea datelor de layout. |
| Accesoriul este deplasat sau scalat gresit | Conversia session -> world -> face-local, scara si offset-urile modelului. |
| Accesoriile se vad prin fata | Materialul depth-only si configuratia shaderului de ocluzie. |

## 20. Varianta de prezentare in aproximativ doua minute

> Prima parte urmareste imagini cunoscute. Am o biblioteca initiala cu trei tinte si creez un cub copil al fiecarei imagini aflate in Tracking. Dictionarul evita duplicatele, iar setul de GUID-uri filtreaza biblioteca activa. Pentru cerinta runtime descarc Footprint, creez o biblioteca mutabila si astept validarea job-ului inainte de activare. Biblioteca anterioara ramane functionala in timpul pregatirii. La comutare refac si continutul trackables deja existente, deoarece nu este garantat un nou eveniment.
>
> A doua parte foloseste camera frontala si ARFaceManager. Masca albastra este materialul aplicat pe mesh-ul dinamic al fetei. Bonusul adauga ochelari, urechi si coroana, selectabile direct sau ciclic, fara sa elimine albastra. Pentru accesorii obtin nasul si reperele fruntii, le convertesc din session space in coordonate locale fetei si calculez pozitia si scara. Nu cer trackingul mediului in modul selfie: verific trackingul fetei. Accesoriile sunt copii ai fetei, iar mesh-ul acesteia foloseste un shader care scrie numai adancime, ca sa ascunda partile virtuale aflate in spatele fetei.

### Checklist

- [ ] Diferentiez biblioteca de referinta de imaginile efectiv urmarite.
- [ ] Inteleg GUID-ul tintei fata de TrackableId-ul instantei.
- [ ] Pot explica download -> validare job -> activare.
- [ ] Stiu de ce runtime este o biblioteca separata si de ce se pastreaza cache.
- [ ] Pot explica bugul comutarii fara un eveniment nou.
- [ ] Diferentiez materialul albastru de prefab-urile accesoriilor.
- [ ] Stiu de ce sunt patru selectii si numai trei prefab-uri.
- [ ] Inteleg diferenta dintre trackingul camerei si trackingul fetei.
- [ ] Pot explica session -> world -> face-local si scalarea din repere.
- [ ] Pot explica ocluzia prin depth-only si curatarea instantelor/bufferului nativ.

## 21. Pregatirea comuna a prezentarii pentru toate laboratoarele

- Foloseste un dispozitiv ARCore compatibil si acorda permisiunea camerei.
- Pregateste o suprafata cu detalii, tintele de imagini si internet pentru laboratorul 3/descarcarea runtime.
- Pastreaza datele aplicatiei daca vrei sa demonstrezi ID-ul Cloud Anchor salvat.
- Verifica manual ca APK-ul instalat este buildul actual, nu unul mai vechi.
- Pentru meniul complet, [SceneManagement.cs](../Assets/Scripts/SceneManagement.cs) incarca scenele prin index:
  - 0: Main Menu;
  - 1: laboratorul 1;
  - 2: laboratorul 2;
  - 3: laboratorul 3;
  - 4: laboratorul 4 - imagini;
  - 5: laboratorul 4 - fete.
- In Build Profiles verifica lista activa de scene si ordinea ei; o selectie care omite sau reordoneaza scenele poate invalida indicii meniului.
- Metodele `Bonus1()` si `Bonus2()` din scriptul de navigare trimit la indicii 6 si 7, dar lista actuala are numai scenele 0-5. Bonusurile prezentate aici sunt integrate in scenele laboratoarelor, nu in doua scene suplimentare.
- Compilarea si verificarile din Editor nu inlocuiesc demonstratia cu trackingul nativ pe tableta.

**Navigare:** [Laboratorul 1](Laboratorul_1.md) | [Laboratorul 2](Laboratorul_2.md) | [Laboratorul 3](Laboratorul_3.md).
