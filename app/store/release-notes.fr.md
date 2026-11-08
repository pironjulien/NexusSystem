# Notes de version de l'appli NexusSystem (Microsoft Store, fr-FR)

Traduction française des sections `## [app a.b.c.d]` de `CHANGELOG.md`, champ « Nouveautés de cette version » de
Partner Center. Une section par version de l'appli, avec les mêmes entrées que l'anglais, ajoutée en même temps que la
section anglaise. `tools/release-notes.ps1` (appelé par `tools/package.ps1 -Store`) en tire
`release-notes.fr.txt` et refuse le paquet si la section de la version manque, n'a pas autant d'entrées que l'anglais
ou dépasse 1 500 caractères.

## [app 1.0.20.0] - 2026-11-08

### Modifié
- La page d'un produit tient dans la fenêtre : « Sur ce PC », puis l'aide et les guides, puis ce qu'ont fait vos IA,
  chaque carte défilant en elle-même quand la fenêtre est basse, et une marge sous la page du produit sur le site.

## [app 1.0.19.0] - 2026-11-08

### Modifié
- L'appli n'extrait et n'exécute plus `get-nexus.ps1` dans `%TEMP%` : quand une ancienne installation n'a pas la tâche
  de maintenance, elle installe le moteur natif de sa version du catalogue (`NexusMaintenance.exe` 1.8.0, vérifié
  avec le `SHA256SUMS.txt` de la version et, quand `gh` est connecté, sa provenance de build) dans
  `%USERPROFILE%\.nexus` et enregistre sa tâche par l'API du Planificateur de tâches, dans le processus de l'appli.
