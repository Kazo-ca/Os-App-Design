> [English version](README.md)

# Kazo Design

L'app de mise en page de Kazo OS (pages et planches, blocs de texte chaînés, styles de paragraphe et de caractère, gabarits, tableaux, export PDF), construite sur [DesignCraft](https://github.com/storytold/designcraft) (équipe ArtCraft), compilée depuis notre fork [`Kazo-ca/designcraft`](https://github.com/Kazo-ca/designcraft). En cours : voir le [plan](doc/fr/plan.md). Ce dépôt a été créé avec le modèle `kazoos-solution` ; la fiche du Store remplace cette page au chantier du Store.

| Dossier | Contenu |
| --- | --- |
| `src/Kazo.Design` | L'app (app fenêtrée ; sa classe `[AppManifest]` génère `manifest.json`) |
| `src/Kazo.Design.Os` | Un hôte qui lance tout Kazo OS avec les apps de base et cette app : le projet à démarrer |
| `tests/Kazo.Design.Tests` | Les tests de l'app (xUnit) |
| `tests/e2e` | Les parcours de bout en bout dans l'hôte Kazo OS (Playwright) : `npm run test:e2e` après `dotnet build Kazo.Design.slnx -c Debug` |
| `external/KazoOs` | Le dépôt Kazo OS en sous-module git, sur un tag de release |
| `external/designcraft` | Les sources de DesignCraft (notre fork `Kazo-ca/designcraft`) en sous-module git, seulement pour compiler DesignCraft (`tools/build-designcraft.sh`, workflow Build DesignCraft) |
| `src/Kazo.Design/wwwroot/designcraft` | Le build web de DesignCraft et sa provenance `designcraft.json`, écrits par `tools/build-designcraft.sh`, jamais modifiés à la main |
| `store/` | Ce que montre le Kazo OS Store : icône (PNG 256×256), descriptions courtes, descriptions, `store.json` |

## Compiler DesignCraft

`tools/build-designcraft.sh` compile le commit de `external/designcraft` avec Trunk 0.21.14, Rust
stable et la cible `wasm32-unknown-unknown`, avec les polices web de `craft-fonts` à la révision
qu'épinglent les versions de DesignCraft, optimisé pour la taille (`opt-level = "s"`, LTO « fat »,
une seule unité de génération, posés par les variables `CARGO_PROFILE_RELEASE_*` sans toucher au
fork), puis remplace `src/Kazo.Design/wwwroot/designcraft/` par le résultat et sa provenance. Le
workflow **Build DesignCraft** (onglet Actions) fait la même chose et ouvre une pull request avec le
build. Pour passer à un autre commit de DesignCraft : déplacer le sous-module sur un commit du fork,
puis recompiler ; un test échoue tant que le build et le sous-module ne concordent pas.

## Premiers pas

```bash
# Un dépôt créé depuis le template GitHub a déjà le sous-module :
git clone --recurse-submodules <votre dépôt>

# Un dossier créé par « dotnet new kazoos-solution » l'ajoute une fois :
git init
git submodule add --depth 1 https://github.com/Kazo-ca/Os.git external/KazoOs
git -C external/KazoOs checkout v<version de Kazo OS>
```

Ouvrez ensuite `Kazo.Design.slnx` dans Visual Studio ou Rider et démarrez `Kazo.Design.Os`,
ou lancez `dotnet run --project src/Kazo.Design.Os`.

## La version de Kazo OS

`external/KazoOs/wasm2/KazoOs/build/Versions.props` est la version de Kazo OS de ce dépôt.
Pour changer de version, placez le sous-module sur son tag et validez-le :

```bash
git -C external/KazoOs fetch --depth 1 origin tag v2.2.0
git -C external/KazoOs checkout v2.2.0
git add external/KazoOs
```

Ne modifiez jamais le sous-module : on ne modifie pas l'OS depuis un dépôt d'app, et la CI refuse
un sous-module modifié ou hors de son tag.

## Deux profils de build

| Profil | Kazo OS vient de | Usage |
| --- | --- | --- |
| `Debug` | les projets du sous-module (`KazoOsUseSource=true`) | entrer dans le SDK et l'OS, points d'arrêt partout |
| `Release` | les paquets `Kazo.Os.*` de nuget.org, à la version du sous-module | CI, `dotnet publish`, le Kazo OS Store |

Forcez l'un ou l'autre avec `-p:KazoOsUseSource=true` ou `false`.

## Publier dans le Kazo OS Store

1. Remplissez `store/` (icône, descriptions courtes, descriptions, catégorie et étiquettes dans `store.json`).
2. Changez `Version` dans la classe `[AppManifest]` de l'app et validez-la dans `main`, puis posez le
   tag : lancez le workflow **Tag a release** (onglet Actions, ou l'API GitHub pour un agent), qui pose
   sur la tête de `main` le tag de la version du manifeste et lance les deux workflows de
   publication ; ou poussez le tag vous-même : `git tag v1.0.0 && git push --tags`.
3. Le workflow **Publish to the Kazo OS Store** compile l'app et produit le dossier du catalogue
   `apps/<appId>_<version>/`. Avec un secret `HACK_STORE_TOKEN` (un jeton qui peut créer un fork et
   ouvrir des pull requests), il ouvre la pull request vers `Kazo-ca/Os-AppMarketPlace` depuis
   votre fork ; sans lui, téléchargez l'artefact du workflow et ouvrez la pull request vous-même.

`HACK_STORE_TOKEN` est un jeton d'accès personnel GitHub à vous, que Kazo OS ne fournit pas :

1. GitHub ▸ **Settings** ▸ **Developer settings** ▸ **Personal access tokens** ▸ **Tokens (classic)** ▸
   **Generate new token (classic)**, avec une expiration courte et les scopes **`repo`** et
   **`workflow`** (un jeton à grain fin ne peut pas couvrir à la fois votre fork et le dépôt du catalogue).
2. Copiez-le (affiché une seule fois) dans ce dépôt : **Settings** ▸ **Secrets and variables** ▸
   **Actions** ▸ **New repository secret**, nom `HACK_STORE_TOKEN` (ou `gh secret set HACK_STORE_TOKEN`).

`repo` donne accès à tous vos dépôts : gardez le jeton dans ce seul secret et révoquez-le au moindre doute.

Le catalogue ne prend jamais de binaire de votre part : une fois votre pull request relue et
fusionnée, sa CI compile l'app au commit du tag.

## Publier sur la branche `kazoos-packages`

Le même tag `v*` lance aussi **Publish the Kazo OS packages** : il fait le paquet de l'app et le
pousse sur la branche `kazoos-packages` de ce dépôt, en gardant les versions déjà publiées. Les
utilisateurs de Kazo OS l'installent alors avec `kazopkg install <owner>/<repo>`, sans le Kazo OS Store.

Ces paquets ne sont pas signés : les apps sont signées seulement dans le dépôt du Kazo OS Store
(`Kazo-ca/Os-AppMarketPlace`), quand il compile votre app depuis le catalogue (ADR 0219). Kazo OS
les vérifie par leur SHA-256 et demande un administrateur avant d'installer depuis une source non
fiable. Pour un paquet signé, installez l'app depuis le Kazo OS Store.
