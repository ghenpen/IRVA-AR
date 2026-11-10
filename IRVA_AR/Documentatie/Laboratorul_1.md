# Laboratorul 1 - AR Foundation, plane si puncte caracteristice

Ghid pentru intelegerea si prezentarea implementarii din proiect. Citeste mai intai sectiunile 1-4, apoi deschide scriptul si urmareste sectiunea 5. La final ai demonstratia, intrebari posibile si o recapitulare.

**Implementat:** configurarea unei scene AR, vizualizarea planelor si a punctelor caracteristice, plus bonusul cu statistici in timp real.

**Scena:** [L1_ARFoundationIntro.unity](../Assets/Scenes/L1_ARFoundationIntro.unity)  
**Cod propriu:** [ARStatisticsDisplay.cs](../Assets/Scripts/ARStatisticsDisplay.cs)

## 1. Ce prezinti, pe scurt

> Aplicatia foloseste camera dispozitivului pentru a observa mediul real. ARCore estimeaza miscarea dispozitivului si identifica repere vizuale si suprafete plane. AR Foundation expune aceste informatii in Unity, iar managerii creeaza reprezentarile vizuale. Bonusul citeste datele existente si afiseaza numarul de plane, numarul punctelor caracteristice si pozitia si orientarea camerei.

Distinctia esentiala: **scriptul de statistici nu detecteaza mediul**. Detectia si trackingul sunt realizate de providerul AR, iar scriptul doar citeste si afiseaza rezultatele.

## 2. Notiunile pe care trebuie sa le intelegi

| Notiune | Explicatie |
|---|---|
| AR, realitate augmentata | Continut virtual suprapus peste imaginea mediului real, cu o legatura spatiala cu acel mediu. |
| AR Foundation | API-ul Unity pentru functionalitati AR, independent de o singura platforma. |
| ARCore | Providerul care ofera functionalitatile AR pe dispozitivul Android compatibil. |
| Tracking | Estimarea si actualizarea pozitiei/orientarii dispozitivului sau ale unui element urmarit. |
| Feature point | Reper vizual pentru care sistemul estimeaza o pozitie 3D. Nu este orice pixel din imagine si nici un obiect intreg recunoscut. |
| Point cloud | O colectie de astfel de pozitii 3D. Un point cloud poate contine multe puncte. |
| Plan | O suprafata aproximata ca plana, de exemplu o masa sau podeaua. Estimarea ei se poate modifica pe masura ce sistemul observa mai mult. |
| Trackable | Un element urmarit de subsistemul AR si reprezentat in Unity: plan, nor de puncte, imagine, fata etc. |
| Pose | Perechea pozitie + rotatie. Nu include scara. |
| Prefab | Un sablon de GameObject si componente din care se pot crea instante. |

ARCore foloseste informatii vizuale si senzoriale pentru estimarea miscarii. Nu ai scris aici un algoritm propriu de computer vision sau SLAM.

O suprafata alba, uniforma sau lucioasa poate fi greu de urmarit. Sistemul are nevoie de detalii vizuale stabile, lumina potrivita si miscari rezonabile ale dispozitivului.

## 3. Ce rol au obiectele si componentele din scena

| Componenta | Rol |
|---|---|
| `ARSession` | Gestioneaza ciclul de viata si starea sesiunii AR. |
| `XROrigin` | Leaga sistemul de coordonate al trackingului de lumea Unity; contine camera si ierarhia pentru trackables. |
| `ARCameraManager` | Interfata cu functionalitatile camerei AR. |
| `ARCameraBackground` | Afiseaza imaginea camerei ca fundal al randarii. |
| `TrackedPoseDriver` | Actualizeaza transformarea camerei folosind datele de tracking. |
| `ARInputManager` | Gestioneaza subsistemul de input XR; nu este scriptul care proceseaza atingerile pe UI. |
| `ARPlaneManager` | Gestioneaza planele detectate si instantele lor. |
| `ARPointCloudManager` | Gestioneaza norii de puncte disponibili. |
| `Canvas` + TextMesh Pro | Afiseaza meniul si statisticile peste imagine. |

Managerilor le sunt atribuite:

- [AR Default Plane.prefab](../Assets/Prefabs/AR%20Default%20Plane.prefab): reprezentarea vizuala a unui plan detectat.
- [AR Default Point Cloud.prefab](../Assets/Prefabs/AR%20Default%20Point%20Cloud.prefab): reprezentarea vizuala a punctelor.

**Managerul detecteaza/gestioneaza, prefab-ul vizualizeaza.** Nu trebuie confundata o instanta pusa manual in scena cu un plan detectat de camera. Managerii creeaza instantele relevante la rulare.

In proiect exista si configurari ARCore Extensions pentru alte functionalitati. Detectia de baza a planelor si punctelor prin AR Foundation nu este acelasi lucru cu serviciul Cloud Anchors din laboratorul 3.

### Coordonate locale si coordonate world

- `transform.position`: pozitia in lumea Unity.
- `transform.localPosition`: pozitia fata de parintele obiectului.
- `XROrigin` poate introduce translatie, rotatie si scara intre spatiul trackingului si lumea Unity.
- In configuratia AR obisnuita, cu scara unitara, o unitate Unity corespunde unui metru.
- Originea scenei nu reprezinta o coordonata GPS si nici un colt al camerei fizice ales automat.

Aceste distinctii devin importante la masuratori si la ancore.

## 4. Fluxul complet al datelor

```text
Camera si senzorii dispozitivului
    -> ARCore estimeaza trackingul si mediul
    -> AR Foundation actualizeaza managerii
    -> managerii actualizeaza trackables si vizualizarile
    -> ARStatisticsDisplay citeste managerii si camera
    -> TextMesh Pro afiseaza statisticile
```

Vizualizarea planelor si punctelor functioneaza prin componentele SDK. Scriptul propriu este necesar pentru bonus, nu pentru implementarea algoritmului de detectie.

## 5. Explicatia codului

Deschide [ARStatisticsDisplay.cs](../Assets/Scripts/ARStatisticsDisplay.cs). Scriptul este suficient de scurt pentru a-l explica integral.

### 5.1. Clasa si referintele

`ARStatisticsDisplay : MonoBehaviour` inseamna ca scriptul poate fi atasat unui GameObject si participa la ciclul de viata Unity.

Cele patru campuri sunt:

| Camp | Ce contine |
|---|---|
| `planeManager` | Referinta la managerul planelor. |
| `pointCloudManager` | Referinta la managerul norilor de puncte. |
| `arCamera` | Transform-ul camerei AR, nu o pozitie copiata o singura data. |
| `statisticsText` | Componenta TMP in care se scrie textul. |

`[SerializeField] private` pastreaza campul privat pentru codul altor clase, dar permite configurarea si salvarea lui prin Inspector.

`RefreshInterval = 0.1f` este intervalul minim dintre doua actualizari ale textului. `nextRefreshTime` retine momentul urmatoarei actualizari.

### 5.2. `Awake()` - verificarea configuratiei

Verifica daca toate cele patru referinte exista.

Daca lipseste una:

1. scrie o eroare explicita in Console;
2. dezactiveaza componenta prin `enabled = false`.

Astfel nu se ajunge la accesarea repetata a unei referinte `null`. Dezactivarea acestui script nu inseamna dezactivarea intregii sesiuni AR.

### 5.3. `LateUpdate()` - cand se actualizeaza afisajul

```csharp
if (Time.unscaledTime < nextRefreshTime)
    return;

nextRefreshTime = Time.unscaledTime + RefreshInterval;
UpdateDisplay();
```

- `LateUpdate` este apelat in fiecare cadru, dupa etapa `Update`.
- `return` opreste actualizarea curenta daca intervalul nu a trecut.
- `Time.unscaledTime` nu depinde de `Time.timeScale`.
- Textul este actualizat aproximativ de cel mult 10 ori pe secunda, in limita cadrelor disponibile, nu obligatoriu in fiecare cadru.

**De ce nu actualizam textul mereu?** Pentru un HUD de statistici, acest ritm este suficient de lizibil si evita actualizari inutile ale textului. Trackingul AR continua independent; nu este redus la 10 actualizari pe secunda.

### 5.4. `UpdateDisplay()` - ce numara

Pentru plane se foloseste:

```csharp
planeManager.trackables.count
```

Pentru puncte se parcurge fiecare point cloud:

```csharp
int pointCount = 0;
foreach (var pointCloud in pointCloudManager.trackables)
{
    var positions = pointCloud.positions;
    if (positions.HasValue)
        pointCount += positions.Value.Length;
}
```

De explicat:

- `foreach` parcurge norii existenti.
- `positions` poate sa nu fie disponibil; de aceea este verificat `HasValue`.
- `Length` este numarul de pozitii din acel nor.
- Se insumeaza punctele tuturor norilor, **nu se afiseaza doar numarul norilor**.
- Numaratoarea reprezinta datele curente ale managerilor, nu totalul tuturor detectiilor de la pornire.
- Codul nu filtreaza separat fiecare trackable dupa `TrackingState`.

Numerele pot scadea: trackables pot fi eliminate sau actualizate, iar planele pot fi reunite de sistem. Nu este un contor care trebuie sa creasca permanent.

### 5.5. Pozitia si orientarea camerei

```csharp
Vector3 position = arCamera.position;
Vector3 rotation = arCamera.eulerAngles;
```

- Pozitia are componentele X, Y, Z in coordonate world.
- Orientarea afisata este exprimata prin unghiuri Euler, in grade.
- Unity foloseste si reprezentarea prin quaternion pentru rotatii; aici Euler este folosit pentru un afisaj mai usor de citit.
- Unghiurile pot trece de la aproape 360 la aproape 0 fara sa insemne o rotire brusca reala.

`statisticsText.SetText(...)` insereaza valorile in sablon. Formatele `0.00` si `0.0` controleaza numarul de zecimale afisate, nu precizia reala a trackingului.

## 6. Cum faci demonstratia

1. Deschizi scena laboratorului 1 pe tableta si permiti accesul la camera.
2. Indrepti camera spre o masa sau podea cu detalii vizuale.
3. Misti lent dispozitivul, inclusiv lateral, pentru a observa suprafata din pozitii diferite.
4. Arati planele si punctele vizualizate.
5. Arati HUD-ul: numarul de plane si puncte se actualizeaza.
6. Deplasezi tableta si explici schimbarea pozitiei.
7. Rotesti tableta si explici schimbarea orientarii.
8. In Editor arati managerii, prefab-urile atribuite si cele patru referinte ale scriptului.

Nu promite un numar fix de plane sau puncte. Acesta depinde de mediu, dispozitiv si starea trackingului.

## 7. Intrebari posibile

**Care este diferenta dintre AR Foundation si ARCore?**  
AR Foundation este interfata Unity; ARCore este providerul Android care implementeaza functionalitatile native.

**Tu ai implementat detectia planelor?**  
Nu. Am configurat componentele SDK si vizualizarea lor. Pentru bonus am implementat citirea si afisarea datelor.

**Un feature point este un pixel?**  
Nu. Este un reper vizual pentru care sistemul estimeaza o pozitie spatiala. Nu fiecare pixel devine un punct urmarit.

**De ce verifici `HasValue`?**  
Pentru ca pozitiile unui nor pot sa nu fie disponibile in acel moment. Accesarea valorii fara verificare nu este sigura.

**De ce folosesti `LateUpdate` si un interval?**  
Citesc starea pentru afisare dupa etapa obisnuita de actualizare si limitez frecventa actualizarii textului, fara sa limitez trackingul.

**De ce nu vad nimic daca pornesc doar in Editor?**  
Rularea in Editor nu garanteaza un provider AR nativ si date reale. Testul relevant pentru aceasta configuratie este pe dispozitivul ARCore compatibil.

**Pozitia afisata este GPS?**  
Nu. Este pozitia camerei in sistemul de coordonate world al scenei Unity.

## 8. Probleme de recunoscut

| Simptom | Ce verifici |
|---|---|
| Nici imagine de camera, nici tracking | Permisiunea pentru camera, dispozitivul compatibil, sesiunea si camera AR. |
| Fundalul merge, dar nu apar vizualizari | Managerii activi, prefab-urile atribuite, lumina si detaliile suprafetei. |
| Vizualizarile apar, dar HUD-ul nu se actualizeaza | Referintele din Inspector, starea componentei de statistici si Console. |
| Numarul de puncte scade | Poate fi normal; afisajul arata colectiile curente, nu istoricul. |
| Apar valori zero doar in Editor | Nu confunda absenta trackingului nativ cu o eroare de numarare. |

## 9. Varianta de prezentare in aproximativ un minut

> In acest laborator am configurat o scena AR cu ARSession, XR Origin si camera AR. ARCore estimeaza mediul, iar AR Foundation expune planele si norii de puncte prin manageri. Managerii folosesc prefab-uri pentru vizualizare. Pentru bonus, scriptul ARStatisticsDisplay citeste numarul curent de plane si insumeaza pozitiile din toti norii de puncte. Citeste si pozitia world si orientarea Euler ale camerei. Afisajul se actualizeaza la intervale de 0,1 secunde, independent de timeScale. Detectia nu este implementata de acest script: el doar prezinta datele SDK-ului.

### Checklist: esti pregatita daca poti explica

- [ ] Diferenta dintre AR Foundation, ARCore si codul propriu.
- [ ] Rolul sesiunii, originii, camerei si managerilor.
- [ ] Diferenta dintre plan, point cloud si feature point.
- [ ] De ce prefab-urile sunt atribuite managerilor.
- [ ] Cele trei metode ale scriptului: `Awake`, `LateUpdate`, `UpdateDisplay`.
- [ ] De ce numarul norilor nu este numarul punctelor.
- [ ] Diferenta dintre coordonate world si coordonate locale.
- [ ] De ce rotatia afisata si precizia afisata nu reprezinta garantii de masurare.

**Continua cu:** [Laboratorul 2](Laboratorul_2.md).
