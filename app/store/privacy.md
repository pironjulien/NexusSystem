# Confidentialité de l'appli NexusSystem (texte de la page /nexussystem/confidentialite/, brouillon)

Texte à publier par la session site sur https://julienpiron.fr/nexussystem/confidentialite/ (français) et sur la version
anglaise du site. Même forme que les pages de confidentialité de NexusKeypad et NexusBrowser. Version 1, à dater du
jour de la publication.

---

## Français

### Politique de confidentialité de l'appli NexusSystem
Dernière mise à jour : <date de publication> (version 1)

**En résumé.** L'appli NexusSystem ne collecte aucune donnée personnelle. Ce qu'elle lit reste sur votre ordinateur. Rien
n'est envoyé au Développeur ni à personne d'autre. Il n'y a ni compte, ni mesure d'audience, ni télémétrie, ni
publicité, ni serveur.

Le responsable de cette politique est Julien Piron (« le Développeur »), auteur de l'appli NexusSystem. L'appli est
distribuée par le Microsoft Store : les traitements propres à Microsoft (votre compte Microsoft, le Store, les
rapports d'incidents de Windows) relèvent de la déclaration de confidentialité de Microsoft. Chaque produit Nexus que
l'appli installe a sa propre politique, publiée sur sa page.

**Ce que l'appli lit, sur votre ordinateur seulement**

Ces lectures servent uniquement à afficher l'état des produits et ce que vos IA ont fait :
- Les IA présentes : la présence des dossiers et des programmes de Claude Code, Codex et Antigravity.
- Leurs configurations, pour savoir si chaque produit y est déclaré : ~/.claude/settings.json, ~/.claude.json, la
  configuration et les extensions de Claude Desktop, ~/.codex/config.toml, les plugins d'Antigravity
  (~/.gemini/config/plugins). L'appli lit seulement les entrées des produits Nexus.
- Les programmes en cours (nom, ligne de commande, programme parent), pour montrer quel serveur Nexus tourne et
  quelle IA l'a lancé.
- Le journal commun des produits (%LOCALAPPDATA%\NexusJournal) : heure, IA, application visée, action, résultat et
  message d'erreur de chaque action de vos IA. L'appli montre les 24 dernières heures.
- L'état du pont local de NexusBrowser (127.0.0.1), avec son jeton (~/.nexus/nexus-browser/bridge-token), pour savoir
  si Chrome y est relié.
- Le dossier des plugins de Logi Options+, pour savoir si NexusKeypad y est installé.

**Ce que l'appli écrit**
- Le journal technique de l'appli (%LOCALAPPDATA%\NexusSystem\app.log) : des erreurs techniques, qui peuvent contenir des
  chemins de fichiers. Il reste sur votre ordinateur.
- L'arrêt d'urgence (~/.nexus/stop) : l'heure de fin de l'arrêt, que les produits lisent.
- Les extensions de Claude Desktop que vous demandez (~/.nexus/claude-desktop), à glisser vous-même dans Claude
  Desktop.
- Le jumelage de Chrome : un code à usage unique (5 minutes), demandé au pont local et remis à l'extension NexusBrowser
  dans votre Chrome. Il ne quitte pas votre ordinateur.
- Les produits que vous choisissez d'installer : l'appli lance leurs installeurs officiels, qui écrivent ce que dit
  la politique de chaque produit.

**Réseau**
L'appli ne contacte que :
- GitHub, pour les installeurs et les fichiers des versions publiées des produits ;
- PyPI et astral.sh, pour uv et les serveurs des produits ;
- winget (Microsoft), pour Node.js quand Codex est présent ;
- 127.0.0.1, pour le pont local de NexusBrowser.
Ces services reçoivent les informations de toute connexion (adresse IP, version demandée) selon leurs propres
politiques. Aucune donnée n'est envoyée au Développeur ni à un service de mesure d'audience.
Ce que vos IA font ensuite avec les produits relève des politiques de confidentialité de ces IA (Anthropic, OpenAI,
Google).

**Vos données, votre contrôle**
Tout reste sur votre ordinateur. Pour retirer l'appli : désinstallez-la depuis Paramètres > Applications. Windows
efface alors ses données propres. Les produits installés restent : chacun se retire comme l'explique sa page.
Supprimez aussi ~/.nexus/claude-desktop si vous ne voulez plus les extensions téléchargées.
Le Développeur ne reçoit aucune donnée : il n'y a rien à demander, corriger ou supprimer de son côté.

**Enfants**
L'appli ne s'adresse pas aux enfants.

**Modifications et contact**
La version en vigueur est publiée sur cette page avec sa date. Questions : GitHub issues
(https://github.com/pironjulien/NexusSystem/issues).

---

## English

### NexusSystem app privacy policy
Last updated: <publication date> (version 1)

**In short.** The NexusSystem app collects no personal data. What it reads stays on your computer. Nothing is sent to the
Developer or to anyone else. There is no account, no analytics, no telemetry, no advertising and no server.

The party responsible for this policy is Julien Piron ("the Developer"), author of the NexusSystem app. The app is
distributed through the Microsoft Store: what Microsoft processes (your Microsoft account, the Store, Windows error
reports) falls under Microsoft's privacy statement. Each Nexus product the app installs has its own policy, published
on its page.

**What the app reads, on your computer only**

These reads only serve to show the products' status and what your AI apps did:
- The AI apps present: whether the folders and programs of Claude Code, Codex and Antigravity exist.
- Their configurations, to know whether each product is declared there: ~/.claude/settings.json, ~/.claude.json,
  Claude Desktop's configuration and extensions, ~/.codex/config.toml, Antigravity's plugins
  (~/.gemini/config/plugins). The app reads only the Nexus products' entries.
- Running programs (name, command line, parent program), to show which Nexus server runs and which AI app started it.
- The products' common journal (%LOCALAPPDATA%\NexusJournal): time, AI app, target application, action, outcome and
  error message of each action of your AI apps. The app shows the last 24 hours.
- The state of NexusBrowser's local bridge (127.0.0.1), with its token (~/.nexus/nexus-browser/bridge-token), to know
  whether Chrome is connected to it.
- Logi Options+'s plugin folder, to know whether NexusKeypad is installed there.

**What the app writes**
- The app's technical log (%LOCALAPPDATA%\NexusSystem\app.log): technical errors, which may contain file paths. It stays on
  your computer.
- The emergency stop (~/.nexus/stop): the time the stop ends, read by the products.
- The Claude Desktop extensions you ask for (~/.nexus/claude-desktop), which you drop on Claude Desktop yourself.
- Chrome pairing: a single-use code (5 minutes), asked from the local bridge and handed to the NexusBrowser extension
  in your Chrome. It never leaves your computer.
- The products you choose to install: the app runs their official installers, which write what each product's policy
  says.

**Network**
The app only contacts:
- GitHub, for the installers and the files of the products' published releases;
- PyPI and astral.sh, for uv and the products' servers;
- winget (Microsoft), for Node.js when Codex is present;
- 127.0.0.1, for NexusBrowser's local bridge.
These services receive what any connection carries (IP address, requested version) under their own policies. No data
is sent to the Developer or to an analytics service.
What your AI apps then do with the products falls under those AI apps' privacy policies (Anthropic, OpenAI, Google).

**Your data, your control**
Everything stays on your computer. To remove the app: uninstall it from Settings > Apps. Windows then deletes its own
data. The installed products stay: each one is removed as its page explains. Also delete ~/.nexus/claude-desktop if
you no longer want the downloaded extensions.
The Developer receives no data: there is nothing to request, correct or delete on their side.

**Children**
The app is not directed at children.

**Changes and contact**
The current version is published on this page with its date. Questions: GitHub issues
(https://github.com/pironjulien/NexusSystem/issues).
