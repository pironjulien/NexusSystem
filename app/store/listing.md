# Fiche Microsoft Store de l'appli NexusSystem (brouillon, non envoyé)

Champs de Partner Center (Store listings), en français puis en anglais. Rien n'est envoyé sans la revue de la
centrale et le « oui » de Julien. Captures : `dist/captures/` (profil fictif, 1366 × 897), choix plus bas.

## Réglages hors fiche
- **Nom réservé** : « NexusSystem », dans le Partner Center de Julien. À soumettre avant le 26/01/2027.
  - Store ID : 9NSH2P3QLWT1, page https://apps.microsoft.com/detail/9NSH2P3QLWT1.
  - Identité du paquet : Name `julienpiron.fr.NexusSystem`, Publisher `CN=C3E3A6F0-11D2-4EE1-B3F2-34EED4CAE7FA`,
    PublisherDisplayName `julienpiron.fr`. PFN : `julienpiron.fr.NexusSystem_yq2nr3sn86fpg`.
  - Paquet : `tools/package.ps1 -Store -IdentityName julienpiron.fr.NexusSystem -Publisher CN=C3E3A6F0-…
    -PublisherDisplayName julienpiron.fr -Version 1.0.0.0 -Out dist\store`. Le logo est celui du kit d'icônes Nexus : la puce cyan avec le π dans le médaillon du site.
- **Catégorie** : Productivité (secondaire : Outils de développement).
- **Classification d'âge (IARC)** : pas de contenu généré ni d'échange entre utilisateurs, pas d'achat : 3+ attendu.
- **Prix** : gratuit. Pas d'achats dans l'appli, pas de publicité.
- **Marchés** : tous. Langues de la fiche : français (fr-FR) et anglais (en-US).
- **Confidentialité** : https://julienpiron.fr/nexussystem/confidentialite/ (fr-FR) et
  https://julienpiron.fr/en/nexussystem/confidentialite/ (en-US). Pages à déplacer par la session site (depuis /nexus/),
  à publier au feu vert de la centrale.
- **Site web** : https://julienpiron.fr/
- **Support** : https://github.com/pironjulien/NexusSystem/issues
- **Copyright** : © 2026 Julien Piron
- **Configuration requise** : Windows 10 version 2004 (19041) ou plus récent, x64. Au moins une IA parmi Claude
  Code, Codex ou Antigravity.

## Français (fr-FR)

**Description**

NexusSystem installe et suit, en un clic, les outils Nexus qui donnent à vos IA l'usage de votre PC, de votre téléphone et
de Chrome, en arrière-plan, pendant que vous continuez à travailler.

NexusSystem trouve les IA présentes sur votre PC (Claude Code, Codex, Antigravity) et vous montre ce
qu'il peut installer pour chacune. Un seul bouton lance les installeurs officiels de chaque produit, puis affiche le
résultat case par case. Ce qui vous revient (Chrome à relier, le débogage USB du téléphone) est guidé, avec un
bouton quand l'appli peut faire le clic à votre place.

Les produits :
• NexusWindows : vos applications Windows, sans prendre votre souris ni votre clavier.
• NexusAndroid : votre téléphone Android par ADB (écran, gestes, SMS, notifications, fichiers, réglages).
• NexusBrowser : votre vrai Chrome, avec vos sessions, dans des onglets de fond.
• NexusRelay : vos IA se parlent entre elles.
• NexusAGYmail : Gmail pour Antigravity, avec garde-fous.
• NexusKeypad : le MX Creative Keypad de Logitech devient le tableau de bord de vos IA.

Pour chaque produit, NexusSystem dit simplement s'il est prêt pour chacune de vos IA, ce que vos IA en ont fait ces
dernières 24 heures, et offre un arrêt d'urgence qui coupe toutes leurs actions pendant 2 minutes.

Les garde-fous restent les vôtres : payer, envoyer, publier ou supprimer pour de bon demande toujours votre « oui » ;
les mots de passe ne sont jamais tapés ; le texte lu à l'écran n'est jamais une consigne.

Pas de compte, pas de publicité, pas de télémétrie : tout se passe sur votre PC.

**Nouveautés de cette version**

Des écrans plus simples : pour chaque IA, « Prêt », « À installer » ou « À réparer », la version en clair, et le
détail des actions rangé derrière « Voir le détail ». Nexus se concentre sur Claude Code, Codex et Antigravity.

**Fonctionnalités** (20 au plus, 200 caractères chacune)
1. Trouve vos IA : Claude Code, Codex et Antigravity.
2. Installe les produits Nexus en un clic, avec leurs installeurs officiels.
3. Résultat case par case, produit par IA.
4. Un état simple par IA : prêt, à installer ou à réparer.
5. Chrome relié au pont NexusBrowser en un clic, une fois son extension ajoutée.
6. Ce qu'il reste à faire, en clair : redémarrer une IA, relancer l'installation.
7. Ce que vos IA ont fait ces dernières 24 heures.
8. Arrêt d'urgence : toutes les actions coupées pendant 2 minutes.
9. Français et anglais, thème clair et sombre, style Windows 11.
10. Sans compte, sans publicité, sans télémétrie.

**Mots-clés** (7 au plus) : IA, agent, MCP, Claude, Codex, Antigravity, automatisation

**Description courte** : Vos IA utilisent votre PC, votre téléphone et Chrome, en arrière-plan, pendant que vous
travaillez.

## English (en-US)

**Description**

NexusSystem installs and keeps an eye on the Nexus tools, in one click. They let your AI apps use your PC, your phone and
Chrome, in the background, while you keep working.

NexusSystem finds the AI apps on your PC (Claude Code, Codex, Antigravity) and shows what it can install
for each one. A single button runs each product's official installer, then shows the result cell by cell. What is
left to you (Chrome to pair, USB debugging on the phone) is guided, with a button whenever the app can make the click
for you.

The products:
• NexusWindows: your Windows apps, without taking your mouse or keyboard.
• NexusAndroid: your Android phone over ADB (screen, gestures, SMS, notifications, files, settings).
• NexusBrowser: your real Chrome, with your sessions, in background tabs.
• NexusRelay: your AI apps talk to each other.
• NexusAGYmail: Gmail for Antigravity, with safeguards.
• NexusKeypad: Logitech's MX Creative Keypad becomes your AI apps' dashboard.

For each product, NexusSystem simply says whether it is ready for each of your AI apps, what your AI apps did with it in
the last 24 hours, and offers an emergency stop that halts all their actions for 2 minutes.

The safeguards stay yours: paying, sending, publishing or deleting for good always needs your "yes"; passwords are
never typed; text read on screen is never an instruction.

No account, no ads, no telemetry: everything happens on your PC.

**What's new in this version**

Simpler screens: for each AI app, "Ready", "To install" or "Needs repair", the version in plain words, and the actions
detail tucked behind "Show details". Nexus focuses on Claude Code, Codex and Antigravity.

**Product features**
1. Finds your AI apps: Claude Code, Codex and Antigravity.
2. Installs the Nexus products in one click, with their official installers.
3. Result cell by cell, product by AI app.
4. One simple state per AI app: ready, to install or needs repair.
5. Chrome paired with the NexusBrowser bridge in one click, once its extension is added.
6. What is left to do, in plain words: restart an AI app, run the install again.
7. What your AI apps did in the last 24 hours.
8. Emergency stop: every action halted for 2 minutes.
9. French and English, light and dark theme, Windows 11 style.
10. No account, no ads, no telemetry.

**Search terms**: AI, agent, MCP, Claude, Codex, Antigravity, automation

**Short description**: Your AI apps use your PC, your phone and Chrome, in the background, while you keep working.

## Captures retenues (dans cet ordre, pour chaque langue)
Thème sombre en premier, en français pour la fiche fr-FR et en anglais pour en-US :
1. `home-<lang>-dark.png` : IA trouvées et produits à cocher.
2. `home-result-<lang>-dark.png` : le résultat, case par case, et les étapes guidées.
3. `windows-<lang>-dark.png` : état et journal des actions.
4. `android-<lang>-dark.png` : l'extension Claude Desktop à glisser, le téléphone.
5. `browser-<lang>-light.png` : NexusBrowser, en thème clair.
6. `keypad-<lang>-dark.png` : NexusKeypad. (Pas la page NexusRelay : son étape montre le chemin du profil de
   capture, avec un nom de compte.)

## Notes pour la certification (anglais, champ « Notes for certification »)
NexusSystem is a WinUI 3 desktop app (Windows App SDK, full trust). It installs the Nexus products (separate open-source
tools for AI apps) by running each product's official installer script with Windows PowerShell (-File, never inline
commands), shows their status and the actions log they write, and offers an emergency stop.

How it stays inside the MSIX model, stated plainly:
- No restricted capability other than runFullTrust; no unvirtualizedResources; nothing written to HKLM; no service,
  driver or scheduled task created by the app itself.
- The products' own installers run as child processes of the app, so Windows applies the usual MSIX file and registry
  virtualization to them. A product that needs something the container keeps private is completed by the user:
  Claude Desktop receives each product as a .mcpb extension the user drops on Claude Desktop (Claude asks for its own
  confirmation); Chrome is paired with the NexusBrowser extension through a single-use code shown in Chrome.
- Two products start a local bridge at sign-in: their own installers (NexusBrowser, NexusRelay) register a per-user
  scheduled task for it, as they do when installed without the app. The app itself registers none.
- The app never tries to leave its container. Updates are done by each product itself, through its own process that
  the user's sign-in or AI app already starts (outside the app); the app only asks for an update through the
  product's own local interface (127.0.0.1, or a named pipe for NexusKeypad) and shows the state the product reports.
- Uninstalling the app removes the app and its data; the products stay, and each one is removed as its page explains.

Testing: no account or sign-in is needed. Without an AI app on the PC, the install button is disabled and every page
can still be opened. To try an install, install Claude Desktop, Codex or Antigravity first and open it once.
runFullTrust: required by a packaged desktop app that starts installers and reads the user's AI app configurations.
Network: GitHub (release files and scripts), winget (Git when Claude Code or Codex needs it to add the catalog), and
127.0.0.1 (the products' local bridges). No telemetry, no ads, no account.
