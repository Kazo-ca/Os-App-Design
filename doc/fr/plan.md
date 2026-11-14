> [English version](../en/plan.md)

# Plan : l'app Kazo Design pour Kazo OS

Étape 0 du [guide de portage des apps Craft](https://github.com/Kazo-ca/Os/blob/main/doc/fr/wasm/dev/craft-app-port-guide.md)
de Kazo OS, écrite le 2026-11-14 (issue `TODO` [#1](https://github.com/Kazo-ca/Os-App-Design/issues/1)).
Ce document fixe le but, ce que la lecture de DesignCraft a montré, les décisions, les questions
posées à Benoit et les chantiers. Chaque chantier est écrit pour être confié tel quel à un fil
d'implémentation. La référence la plus proche est Kazo Photo
([plan](https://github.com/Kazo-ca/Os-App-Photo/blob/photo/base/doc/fr/plan.md)) : même manque en
amont, même chemin (fork, puis app). Kazo Fig
([plan](https://github.com/Kazo-ca/Os-App-Fig/blob/main/doc/fr/plan.md)) sert pour les onglets
liés par identifiant.

## But

Kazo Design est **l'app de mise en page de Kazo OS** : pages et planches, blocs de texte chaînés,
styles de paragraphe et de caractère, gabarits, tableaux, export PDF. Il emballe le build web de
[DesignCraft](https://github.com/storytold/designcraft) (équipe ArtCraft, `MIT OR Apache-2.0`,
Rust et egui) et le branche sur les fichiers, les dialogues et les associations de Kazo OS. Kazo
Word reste le traitement de texte ; Kazo Design fait les brochures, magazines, affiches et livres.

**Ce qui distingue ce portage** : comme PhotoCraft, DesignCraft n'a **ni mode hébergé
(`craft-web-host`) ni API de commandes (`craft-web-api`)**. Il faut les écrire dans notre fork
[`Kazo-ca/designcraft`](https://github.com/Kazo-ca/designcraft) avant que la fenêtre Kazo puisse
ouvrir ou enregistrer. Kazo Design est donc, dès sa première version, un build **modifié** compilé
depuis le fork. Le travail dans le fork est plus petit que pour PhotoCraft : DesignCraft sépare déjà
l'enregistrement du document des exports. En revanche son `.wasm` est plus lourd (voir « Essai »).

Le code Kazo commun (iframe, proxy, sélecteur de fichiers, raccourcis, `DocumentSession`, exports)
vient du module `Kazo.Os.AppSdk.Craft` de Kazo OS
([ADR 0259](https://github.com/Kazo-ca/Os/blob/main/doc/fr/wasm/dev/adr/0259-shared-craft-app-host-module.md),
[ADR 0264](https://github.com/Kazo-ca/Os/blob/main/doc/fr/wasm/dev/adr/0264-craft-app-exports-reach-the-window.md)),
publié dans **Kazo OS 3.3.4**, la version minimale de Kazo Design.

## Lecture de DesignCraft (étape 0, 2026-11-14)

Lue sur `main` du fork (commit `437c532`, 2026-11-12, version 0.20.1) et compilée depuis ce
commit. L'étiquette amont `v0.20.1` est le commit `33a4678` de la branche `release`, que le fork
n'a pas encore.

| Point | Constat |
| --- | --- |
| Build web | `apps/designcraft-web` (Trunk, `public_url = "./"`, `wasm-opt -Oz`), eframe sur **wgpu** : WebGPU, repli WebGL 2 (`?webgl`). `?sample` ouvre le magazine d'exemple. Rust 1.95 au minimum. Les langues sont dans le code (`crates/ui-egui/src/i18n/`) : pas de `craft-i18n` à fournir. |
| Mode hébergé | **Absent.** `apps/designcraft-web/src/web.rs` (450 lignes) ne connaît que le navigateur : Ouvrir et Importer = sélecteur du navigateur (`rfd`), Enregistrer et exports = téléchargement. |
| API de commandes | **Absente** dans le build web (`window.designcraft` n'existe pas), mais le socle est là : `DesignApp::with_control` prend une file de `ControlRequest`, la même que le serveur TCP du bureau (`docs/control-protocol.md`). Il manque le branchement `craft-web-api`. Différence avec PhotoCraft : pas de `Dispatch::give_up` ; une requête abandonnée par la page serait quand même exécutée (à traiter dans F1). |
| Canevas | `designcraft_canvas`, écrit en dur : une instance par document HTML, donc une iframe par fenêtre. La page porte `#designcraft_loading` ; le panneau d'erreur graphique (`#designcraft_graphics_error`) est créé par le code. |
| Zip officiel | `designcraft-web-<version>.zip` (`packaging/web/package.sh`) : la sortie de Trunk telle quelle, non découpée. Inutile ici faute de mode hébergé ; on compile le fork. |
| Enregistrer le document | **Déjà séparé des exports** : en présence du service `Services::download`, Enregistrer et Enregistrer sous passent tous deux par `download_document` (`crates/ui-egui/src/menus/export.rs`), qui télécharge `<titre>.designcraft` puis marque aussitôt le document enregistré (`file.saveAs { path: nom }`). Sur le web, **Enregistrer sous ne demande rien** et fait la même chose qu'Enregistrer. |
| Format du document | Toujours `.designcraft` (natif). IDML, PDF, EPUB, etc. sont des exports, jamais le document. |
| « Modifié » | Le document est marqué propre **avant** que le fichier soit écrit quelque part (le navigateur ne confirme rien). C'est le piège « propre avant l'écriture » (craft-libs #611) : en mode hébergé il faut attendre `designcraft:saved`. |
| Exports | Par `Services::write(chemin, octets)` après `ask_save`, qui sur le web ne demande rien : PDF, PNG d'une page, IDML, EPUB (fluide et fixe), HTML, PowerPoint, XML, texte (`.txt`, `.rtf`), livret PDF, nuancier `.ase`, jeu de raccourcis. L'export par lot utilise `download`. Enregistrer une copie (`file.saveACopy`) écrit aussi par ce chemin. Tous finissent en `<a download>`. |
| Ouvrir | `file.openBytes { name, base64 }` : `.designcraft`, `.idml`, et `.indd` / `.indt` (convertis à l'ouverture). Le titre du document est le nom du fichier sans son extension ; le document ouvert par octets n'a pas de chemin. Les fichiers arrivent par une boîte (`Services::inbox`, `ImportedFile`) avec la demande d'origine. |
| Importer (Place) | Images (`png jpg jpeg gif webp tif tiff bmp psd svg`), `pdf`, `ai`, texte (`txt docx rtf md`), `xlsx`, et aussi `idml` / `designcraft`. Sélecteur du navigateur ; les octets sont incorporés au document. Autres demandes de fichier : nuancier `.ase`, raccourcis (`.json`, `.txt`), styles d'un autre document. |
| Documents | Plusieurs documents en **onglets** dans une page. Commande `document.list` : `[{ index, title, dirty, active }]`. Le moteur a un identifiant stable par document (`uid`) et un chemin (`path`), mais `document.list` ne les donne pas : à ajouter (F1). `file.activate { index }`, `file.close { index? }`. |
| Commandes | `engine.execute { command, params }`, `ui.menu.invoke`, `engine.commands`, `document.inspect`, `ui.inspect`, `ui.set`, `ui.screenshot`, `ui.render`, `ui.dialog.*`. Fichier : `file.new`, `file.newSample`, `file.openBytes`, `file.save`, `file.saveAs`, `file.saveACopy`, `file.serialize` (le `.designcraft` en base64), `file.close`, `file.activate`, `file.revert`, `file.export*`. Interface : `app.save`, `app.saveDialog`, `app.openDialog`, `app.placeDialog`, `app.export*`. |
| Thème | `ui.set { brightness }` (commande `window.brightness`) : `dark`, `mediumDark` (défaut), `mediumLight`, `light`, `highContrast`. |
| Langue | `app.language { lang }` : anglais (`""`), `de fr es ja zh ar pt-br it uk ru cs pl ka hu sv`. |
| Réglages | Le build web **ne garde rien** entre deux sessions (voir « Essai » : aucun `localStorage`). Préférences, espace de travail et fichiers récents repartent de zéro à chaque fenêtre. |
| Polices | Le build web n'a que ses polices intégrées : **Inter, Source Sans 3, Source Serif 4, JetBrains Mono** ; avec `CRAFT_FONTS_DIR` à la compilation (comme les versions officielles), s'ajoutent BIZ UDPGothic (japonais) et Noto Sans Georgian. Pas de polices du système sur le web, pas de chargement à la demande (PhotoCraft en a un, DesignCraft non). Un document qui demande une autre police la signale manquante. Question Q1. |
| Arrêt graphique | Si le navigateur perd WebGPU ou WebGL, un panneau HTML remplace l'app et propose « Save a Copy » pour chaque document ouvert (téléchargement), puis « Reload ». |
| Fermeture de page | Pas d'avertissement `beforeunload` ; la garde de fermeture de la fenêtre Kazo est la seule. |
| Taille de fenêtre | Bureau : 1440 × 900, minimum 900 × 560. |
| Marque | Le nom et les logos ArtCraft sont des marques (`docs/brand/LICENSE-brand.txt`) : un build modifié doit les retirer. La fenêtre À propos dessine la marque ArtCraft, la phrase « Part of the ArtCraft suite », un bouton Discord et la liste des autres apps ; le menu Aide porte cinq liens ArtCraft (`help.discord`, `help.appPage`, `help.github`, `help.issues`, `help.website`) ; la barre du haut montre un bouton « Discord » en permanence ; l'écran d'accueil porte un encart « Join the ArtCraft community » avec la marque et trois liens (voir « Essai »). |
| Règles du dépôt amont | `AGENTS.md` : Rust seulement, pas de `unwrap`/`panic` hors tests, tout est une commande, ne jamais casser wasm (`cargo xtask wasm`), `cargo xtask ci` avant chaque commit, pas de changement de CI dans une PR de fonctionnalité, pas de mention d'agent, une issue avant une PR. |

### Essai (2026-11-14)

Fait sur le fork (`main`, `437c532`, v0.20.1), dans le conteneur de cette session (4 cœurs, Linux), sans aucun changement au code.

| Point | Résultat |
| --- | --- |
| Outils | Rust stable 1.97.0, cible `wasm32-unknown-unknown`, Trunk 0.21.14. Aucune entrée externe (`craft-fonts` non fourni). |
| Commande | `trunk build --release` dans `apps/designcraft-web` (profil `release` du dépôt : `opt-level = 3`, LTO « thin »). |
| Durée | 9 minutes à froid : 6 min de `cargo`, puis 3 min de `wasm-opt -Oz`. |
| Sortie | `dist/web` : `index.html`, un `.js` de 168 Ko, un `.wasm` de **62,3 Mo (59,4 Mio)**, 24,2 Mo en gzip, un dossier `snippets/`. Le « 13 MB » de `packaging/web/README.md` n'est plus vrai. Sections : 45 Mo de code, 17 Mo de données. |
| Limites | Sous les limites de Kazo OS (128 Mio par fichier ; paquet de 64 Mio compressé, 128 Mio décompressé), mais **au-dessus de l'avertissement de GitHub à 50 Mio** par fichier (refus à 100 Mio). DesignCraft n'a pas de profil « taille » comme le `wasm-release` de PhotoCraft : le chantier 1 mesure un build `opt-level = "s"` (variables `CARGO_PROFILE_RELEASE_*`, sans toucher au fork) et choisit. |
| Démarrage | Chromium sans carte graphique (`?webgl`, WebGL 2 par SwiftShader) : l'app s'affiche en moins d'une seconde depuis un serveur local. `?sample` ouvre le magazine d'exemple. |
| Enregistrer | Ctrl+S sur le magazine d'exemple : téléchargement de `Quarterly — Spring Issue.designcraft` (832 Ko), sans demande. |
| API de page | `typeof window.designcraft` vaut `undefined`. |
| Stockage | `localStorage` vide après usage : rien n'est conservé. |
| Console | Aucune erreur, aucune requête en échec. |

Vu à l'écran : un bouton « Discord » en permanence dans la barre du haut, et sur l'écran d'accueil un encart « Join the ArtCraft community » (marque ArtCraft, liens « DesignCraft page », « GitHub », « getartcraft.com », bouton « Join our Discord ») : H9. La fenêtre de 1280 × 820 est bien remplie (outils, page, panneau Propriétés).

Captures : `design/trials/` dans les fichiers du projet (hors dépôt).

## Ce qu'il faut ajouter à DesignCraft (fork)

Le contrat est celui de `craft-web-host` (`storytold/craft-libs`, version « 1 »), avec le préfixe
`designcraft:`. Le code vit dans `apps/designcraft-web`, sur le modèle de
`apps/photocraft-web/src/{api,host,hostmode}.rs` de la branche `kazo/base` de
[`Kazo-ca/photocraft`](https://github.com/Kazo-ca/photocraft/tree/kazo/base/apps/photocraft-web/src)
(lui-même fait sur `apps/gridcraft-web`). Les points marqués ★ demandent une petite ouverture
hors de la coquille web (`crates/ui-egui` ou `crates/engine`).

| # | Comportement | Pourquoi |
| --- | --- | --- |
| H1 | `designcraft:ready { version: "1", appVersion, file: false }` quand l'app écoute ; `designcraft:failed { error }` si elle ne démarre pas, **et quand le rendu est perdu** (là où le panneau « Graphics connection lost » s'affiche). | La fenêtre Kazo sait quand envoyer le fichier et quand montrer une erreur. |
| H2 | `designcraft:open { name, bytes }` ouvre le fichier comme Fichier › Ouvrir, dans un nouvel onglet, refusé au-delà d'une limite de taille avant d'être copié. ★ Un `.designcraft` remis par l'hôte **garde son nom comme chemin** ; un `.idml`, `.indd` ou `.indt` n'en a pas (il est converti, son premier enregistrement crée un `.designcraft`). | Enregistrer réécrit le fichier d'origine seulement quand c'est bien lui. |
| H3 | ★ En mode hébergé, l'enregistrement du document part en `designcraft:save { name, bytes, saveAs, id }` au lieu du téléchargement. `saveAs` est faux quand le document est réécrit sous son chemin, vrai dans les autres cas : nouveau document, document venu d'un IDML ou d'un INDD, et **Enregistrer sous** (qui doit redevenir distinct d'Enregistrer). | L'hôte sait s'il réécrit le fichier lié ou s'il demande où enregistrer. |
| H4 | ★ Le document n'est marqué enregistré qu'à la réception de `designcraft:saved { id }` ; sans réponse il reste modifié. | Corrige « propre avant l'écriture » : un dialogue annulé côté Kazo ne perd rien. |
| H5 | Les **exports, copies et fichiers annexes** (`Services::write`, export par lot, « Save a Copy » du panneau de secours) gardent le téléchargement. | Le module Kazo les intercepte et les remet à la fenêtre comme copies (ADR 0264). |
| H6 | `designcraft:dirty { dirty }` quand un des documents ouverts a des modifications, ou n'en a plus. | Titre de la fenêtre. |
| H7 | `window.designcraft` (`craft-web-api` : `request`, `execute`, `commands`, `docInspect`, `uiInspect`, `screenshot`) sur la file de contrôle, installé avec `?control`. ★ Une requête abandonnée par la page n'est pas exécutée (équivalent de `Dispatch::give_up` de PhotoCraft). ★ `document.list` donne aussi `uid` et `path`. | Thème, langue, liste des documents liée par identifiant stable, garde de fermeture. |
| H8 | Hors mode hébergé, rien ne change. | Le patch reste proposable en amont. |
| H9 | En mode hébergé : la fenêtre À propos ne dessine pas la marque ArtCraft et remplace sa phrase et ses liens par le texte permis (« basé sur DesignCraft de l'équipe ArtCraft ») ; les cinq liens ArtCraft du menu Aide, « Check for Updates », le bouton « Discord » de la barre du haut et l'encart « Join the ArtCraft community » de l'écran d'accueil ne sont pas montrés. | Build modifié. |

Tests demandés dans le fork : tests unitaires des règles pures (`saveAs`, chemin gardé, limite de
taille, attente de `saved`), `cargo xtask ci` (fmt, clippy `-D warnings`, tests, assets, layers,
wasm), un build Trunk, et un parcours Playwright sur une page d'essai qui joue l'hôte (`open`,
`save`, `saved`, `dirty`, `failed`, `window.designcraft.request`).

## Décisions

Les décisions 2, 6 et 7 reprennent les réponses de Benoit pour Kazo Photo (Q1 à Q3 = A,
2026-11-13) ; le point 2 est aussi la consigne donnée à ce plan.

| # | Sujet | Décision |
| --- | --- | --- |
| 1 | Nom et identité | « **Kazo Design** ». Id `ca.kazo.design`, éditeur `pub.kazo`, assembly `Kazo.Design`, catégorie `graphics`, plusieurs fenêtres, fenêtre de 1280 × 820, contenu minimum 900 × 560, `canChangeScreen: false` (ADR 0263). |
| 2 | Où vit le mode hébergé | Dans notre fork maintenant, proposé en amont en parallèle (brouillons d'issues en anglais, publiés par Benoit). |
| 3 | Code commun | Module `Kazo.Os.AppSdk.Craft`, **Kazo OS 3.3.4** au minimum (exports comme copies, ADR 0264). Sous-module `external/KazoOs` sur l'étiquette `v3.3.4`, paquets de nuget.org en CI, dès le chantier 1. |
| 4 | Provenance du build | Toujours compilé depuis le fork : branche `kazo/v0.20.1` (= étiquette amont `v0.20.1`, commit `33a4678`), branche d'intégration `kazo/base` créée d'elle. Sous-module `external/designcraft` sur `kazo/base`. Sortie de Trunk copiée dans `wwwroot/designcraft/` avec `designcraft.json` (version, commit, noms et SHA-256), dans le dépôt : le Store ne compile pas de Rust. |
| 5 | Page de l'iframe | `wwwroot/host/index.html` : canevas `designcraft_canvas` avec `data-host="page"`, `data-craft-app="designcraft"`, `data-craft-build="../designcraft/designcraft.json"`, plus `#designcraft_loading`. Paramètre `control`. |
| 6 | Onglets | On garde les onglets de DesignCraft : une fenêtre tient plusieurs documents, chaque onglet lié à son fichier par l'`uid` du document (H7). Un double-clic dans l'explorateur ouvre une nouvelle fenêtre. Un fichier déjà ouvert dans la fenêtre ramène son onglet. |
| 7 | Associations | App par défaut (`open`, `edit`) pour `.designcraft`, `.idml`, `.indd`, `.indt` : rien d'autre ne les ouvre dans Kazo OS. Aucune association pour les formats qu'on importe (images, `.docx`, `.pdf`…) : ils s'importent dans un document, ils ne s'ouvrent pas. |
| 8 | Enregistrement | `Saving` : règle du module (`DocumentSession.Decide(name, saveAs)`) ; la fenêtre renvoie `true` seulement quand le fichier est écrit, ce qui déclenche `saved` (H4). Un document venu d'un `.idml` ou `.indd` s'enregistre en `.designcraft` par le dialogue de l'OS, dans le dossier du fichier d'origine, qui n'est jamais réécrit. `Exporting` : toujours une copie (`DecideCopy`). |
| 9 | Garde de fermeture | Lue dans `document.list` (`uid`, `title`, `dirty`) ; la fenêtre active chaque document modifié (`file.activate`), lance `app.save` et attend la fin de ses écritures. |
| 10 | Thème et langue | `ui.set { brightness }` : `dark` pour le thème sombre de l'OS, `light` pour le clair, à chaque changement. Langue : `app.language` au démarrage (`CraftLanguage.Pick` sur les 15 codes ; anglais sinon). Comme DesignCraft ne garde rien, la fenêtre les repose à chaque démarrage. |
| 11 | Réglages | Rien à conserver par utilisateur dans la v1 : le build web n'écrit aucun stockage. Limite écrite dans la fiche (espace de travail et préférences repartent des valeurs par défaut). Si l'amont ajoute un stockage, `WebStorageFile` du module le prendra. |
| 12 | Raccourcis | `ReservedKeys.Os` **sans** `Ctrl+N` (Nouveau document). Les conflits entre les accords de l'OS et ceux de DesignCraft (`Ctrl+F6`, `Alt+Shift+F11`, `Shift+F9`…) sont relevés au chantier 2 et notés dans la fiche. |
| 13 | Grands fichiers | La limite de Kazo OS (128 Mio par fichier, ADR 0245) s'applique. Le chantier 2 mesure l'ouverture et l'enregistrement d'un document avec des images incorporées de 30 à 50 Mo. |
| 14 | Piste amont | Brouillons d'issues en anglais, sans mention d'agent, dans `/mnt/project-files/design/upstream/` ; Benoit les publie. Les branches du fork sont écrites pour pouvoir devenir des PR amont. |
| 15 | ADR de Kazo OS | Aucune prévue : le module a ce qu'il faut. Si un chantier en demande une, prochain numéro libre **0271** au 2026-11-14 (à revérifier sur `main`, les PR ouvertes et toutes les branches `*/base` au moment de l'écrire ; Kazo Deck et Kazo Vector avancent en parallèle). |

## Questions posées à Benoit

Posée dans le fil le 2026-11-14.

**Q1. Polices des documents.** Le build web de DesignCraft n'a que quatre familles (Inter, Source
Sans 3, Source Serif 4, JetBrains Mono) et ne lit pas les polices du système. Pour une app de mise
en page, c'est la limite la plus visible : un `.idml` venu d'ailleurs affichera presque toujours
des polices manquantes.

- **A (recommandée)** : v1 avec les polices du build officiel (les quatre familles, plus le
  japonais et le géorgien de `craft-fonts`), limite écrite dans la fiche ; puis un chantier de
  fork **après** la sortie (F3) qui ajoute le chargement de polices à la demande, sur le modèle de
  PhotoCraft, avec les polices latines de `craft-fonts` déjà utilisées par Kazo Photo (15 familles,
  19 Mo). Kazo Design sort plus tôt, et F3 est proposable en amont.
- B : F3 avant la sortie. La v1 a tout de suite une vingtaine de familles, mais elle attend un
  chantier Rust de plus dans le moteur de polices de DesignCraft, le plus incertain du plan.
- C : intégrer les polices latines de `craft-fonts` dans le `.wasm` à la compilation. Simple
  (une condition à changer dans `crates/fonts/build.rs`), mais le `.wasm` grossit d'environ 19 Mo,
  chargés à chaque démarrage, et ce patch n'est pas proposable en amont.
- D : quatre familles seulement, sans suite prévue.

## Chantiers

Deux dépôts : le fork `Kazo-ca/designcraft` (Rust) et `Kazo-ca/Os-App-Design` (C#). Dans chacun,
une branche d'intégration, une branche et une PR par chantier ; un fil d'implémentation par
chantier. Benoit fusionne `design/base` dans `main` ; `main` du fork reste la copie de l'amont.

| N° | Dépôt, branche (vers) | Contenu | Dépend de |
| --- | --- | --- | --- |
| 0 | Os-App-Design, `design/1-plan` (`design/base`) | ce plan (fr et en) | — |
| F1 | designcraft, `kazo/1-web-api` (`kazo/base`) | H7 : `window.designcraft`, `document.list` avec `uid` et `path` | plan fusionné |
| F2 | designcraft, `kazo/2-web-host` (`kazo/base`) | H1 à H6, H8, H9 : mode hébergé | F1 (mêmes fichiers) |
| 1 | Os-App-Design, `design/2-solution` (`design/base`) | solution, manifeste, compilation depuis le fork, page d'iframe, fenêtre qui démarre | plan fusionné ; « terminé » attend F2 |
| 2 | Os-App-Design, `design/3-window` (`design/base`) | ouvrir, enregistrer, onglets, exports, garde de fermeture, sélecteur, raccourcis, thème, langue | 1 et F2 |
| 3 | Os-App-Design, `design/4-store` (`design/base`) | fiche, README, licences, mesures, tag, catalogue, `features.md` | 2 |
| F3 | designcraft, `kazo/3-served-fonts` (`kazo/base`), puis Os-App-Design `design/5-fonts` | polices à la demande (selon Q1) | 3 si Q1 = A ; 2 si Q1 = B |

**Ordre** : F1 puis F2 dans un même fil ; le chantier 1 dans un deuxième fil, en même temps ; puis
2 ; puis 3. Kazo OS 3.3.4 est déjà publié : rien n'attend une version de l'OS.

### F1. `window.designcraft` (fork)

- Créer `kazo/v0.20.1` sur le commit amont `33a467872268c98536231491865b4011b0275b17`
  (`git fetch https://github.com/storytold/designcraft refs/tags/v0.20.1`), puis `kazo/base` de
  cette branche. Pousser les deux.
- `apps/designcraft-web/src/api.rs` sur le modèle de `apps/photocraft-web/src/api.rs` du fork
  PhotoCraft : une file `ControlRequest` donnée à l'app par `DesignApp::with_control`,
  `craft_web_api::page::PageApi::new("designcraft", …).install()`, installé avec `?control`.
  `craft-web-api` épinglé à une révision de `storytold/craft-libs`, comme les autres `craft-*`.
- ★ Une requête que la page abandonne n'est pas exécutée : DesignCraft n'a pas de
  `Dispatch::give_up`, il faut la plus petite ouverture dans `crates/ui-egui/src/control.rs` ou
  `app/control.rs` (lire comment PhotoCraft le fait).
- ★ `document.list` (`crates/engine/src/cmd/inspect.rs`) : ajouter `uid` et `path` à chaque
  document, avec un test.
- `ui.screenshot` sans chemin doit rendre le PNG à la page (vérifier, adapter comme `adapt` de
  PhotoCraft).
- **Terminé quand** : dans une page servie par `trunk serve`,
  `await window.designcraft.request("engine.execute", { command: "document.list" })` répond avec
  `uid`, `ui.set { brightness: "light" }` change le thème, `app.language { lang: "fr" }` change la
  langue, et `cargo xtask ci` passe.

### F2. Mode hébergé (fork)

- `apps/designcraft-web/src/host.rs` et `hostmode.rs` sur le modèle du fork PhotoCraft :
  `Host::connect("designcraft", canvas)`, écoute de `open` et `saved`, `post_ready`, `post_save`,
  `post_dirty`, `post_failed`.
- H2 : le fichier de l'hôte entre par la boîte (`ImportedFile` avec `import_request("open")`),
  comme un fichier choisi. ★ Un `.designcraft` garde son nom comme chemin.
- H3, H4 ★ : aujourd'hui `download_document` télécharge puis marque enregistré. En mode hébergé,
  un service distinct (forme à choisir après lecture de `menus/export.rs`, `menus/run.rs`
  (`save_then`, `app.save`, `app.saveDialog`) et `services.rs`) remet les octets à l'hôte avec
  `saveAs`, et le document n'est marqué enregistré (avec son chemin) qu'à `saved { id }`.
  `save_then` (enregistrer puis fermer, puis exporter…) ne doit lancer sa suite qu'après la
  confirmation. Enregistrer sous envoie `saveAs: true`.
- H5 : ne rien changer à `Services::write` ni à l'export par lot.
- H1 : `post_failed` depuis `graphics_failed` ; H9 ; une ligne dans la doc du build web
  (`packaging/web/README.md`) qui décrit le mode hébergé.
- **Terminé quand** : la page d'essai qui joue l'hôte ouvre un `.designcraft` et un `.idml`,
  reçoit `save` avec le bon `saveAs` dans les quatre cas (réécriture d'un `.designcraft`, document
  venu d'un `.idml`, nouveau document, Enregistrer sous), le document ne redevient propre qu'après
  `saved`, Exporter PDF télécharge toujours, la même page sans `data-host` se comporte comme
  avant, et le fil donne le commit de `kazo/base` à épingler.

### 1. Solution et compilation (`design/2-solution`)

- Solution renommée (`Kazo.Design`, `Kazo.Design.Os`, `Kazo.Design.Tests`), `[AppManifest]` de la
  décision 1, capacités de Kazo Photo, associations de la décision 7.
- Sous-module `external/KazoOs` sur `v3.3.4`, sous-module `external/designcraft` sur `kazo/base`
  (sur `kazo/v0.20.1` tant que F2 n'est pas fusionné).
- `tools/build-designcraft.sh` (Rust stable, cible `wasm32-unknown-unknown`, Trunk 0.21.14,
  `trunk build --release` dans `apps/designcraft-web`, `CRAFT_FONTS_DIR` sur `craft-fonts` à la
  révision de `release.yml` de DesignCraft pour le japonais et le géorgien), `designcraft.json`,
  workflow `build-designcraft.yml`, test xUnit « le commit de `designcraft.json` est celui du
  sous-module ». Modèle : `Os-App-Photo` (`tools/build-photocraft.sh`).
- Page d'iframe (décision 5) et fenêtre sur `CraftFrame` et `FrameStartup` : chargement, app,
  erreur, « Réessayer », thème et langue à `ready`.
- **Terminé quand** : l'app démarre en WebGL 2 (et WebGPU si disponible), deux fenêtres tournent
  ensemble, le paquet `.kazoapp` s'installe dans Kazo OS 3.3.4 publié ; taille du paquet et
  mémoire par fenêtre notées ici.

### 2. Fenêtre (`design/3-window`)

- Ouverture : fichier demandé au lancement, Fichier › Ouvrir par le dialogue de l'OS, décision 6.
- Enregistrement : décision 8, les quatre cas de F2 de bout en bout dans Kazo OS ; un dialogue
  annulé ou une écriture refusée laisse le document modifié.
- Onglets liés par `uid`, titre de la fenêtre sur l'onglet actif, garde de fermeture (décision 9).
- Exports comme copies (`Exporting`) : PDF, IDML, EPUB, PNG, etc. ; aucun téléchargement du
  navigateur ne sort de l'app. Sélecteur de fichiers (`PickRequested`) pour Importer, nuanciers,
  styles et raccourcis. Raccourcis (décision 12), thème et langue suivis en direct (décision 10).
- Mesure de la décision 13.
- **Terminé quand** : aucun cas ne perd de modifications sans que l'utilisateur l'ait choisi,
  aucun dialogue ni téléchargement du navigateur ne sort de l'app, et les raccourcis de l'OS
  marchent le curseur dans la page.

### 3. Store (`design/4-store`)

- Icône sans marque ArtCraft, fiche fr et en (« basé sur DesignCraft de l'équipe ArtCraft »,
  limites connues : polices, réglages non conservés, 128 Mio par fichier, raccourcis gardés par
  l'OS), captures, README refait sur la fiche (`store:readme`, « Essayer » avec
  `?autolaunch=ca.kazo.design`), `THIRD-PARTY-NOTICES`.
- Parcours dans Kazo OS 3.3.4 publié (`tests/installed`), workflow « Tag a release », PR au
  catalogue `Os-AppMarketPlace` (signature seulement là-bas), ligne de `features.md` dans
  `Kazo-ca/Os` (fr et en).
- **Terminé quand** : Kazo Design s'installe depuis le Store, ouvre un `.designcraft` par
  double-clic, l'enregistre, et exporte un PDF dans un dossier de l'utilisateur.

### F3. Polices à la demande (selon Q1)

- Fork : chargement d'une famille à son premier usage depuis `fonts/manifest.txt` relatif à la
  page, sur le modèle de PhotoCraft ; la liste des polices montre les familles servies avant
  qu'elles soient chargées.
- App : `tools/vendor-fonts.sh` de Kazo Photo (polices latines de `craft-fonts`, OFL), licences
  dans `THIRD-PARTY-NOTICES`.

## Piste en amont

Brouillons en anglais, publiés par Benoit, dans `/mnt/project-files/design/upstream/` :
DesignCraft, mode hébergé du build web (`craft-web-host`) ; DesignCraft, `window.designcraft`
(`craft-web-api`) et `document.list` avec `uid`. Non vérifié : les issues déjà ouvertes en amont
(`storytold` est hors de portée de la session pour les issues) ; Benoit regarde avant de publier.

## Avancement

- [x] Étape 0 : lecture de DesignCraft 0.20.1, compilation et essai du build web.
- [x] Plan fr et en.
- [ ] Réponse de Benoit à Q1.
- [ ] F1, F2 (fork).
- [ ] Chantier 1 : solution, compilation, fenêtre qui démarre.
- [ ] Chantier 2 : fenêtre, ouverture et enregistrement, exports.
- [ ] Chantier 3 : Store.
- [ ] F3 : polices à la demande.
