# Politique de sécurité

## Versions concernées

Sollicito est en conception : aucune version n'est encore publiée. Cette politique s'appliquera à la dernière version publiée.

## Signaler une vulnérabilité

**N'ouvrez pas d'issue publique.** Utilisez le signalement privé de GitHub : bouton **Report a vulnerability**, dans l'onglet de sécurité du dépôt. Seuls vous et le mainteneur voyez le rapport, et l'échange se poursuit dans cet outil.

Pour aider à reproduire, indiquez :

- la version concernée et la version de Windows ;
- les étapes qui mènent au problème ;
- l'effet constaté et l'effet attendu.

N'incluez aucune donnée personnelle réelle, ni la vôtre ni celle d'un tiers.

## Périmètre

Sont notamment concernés :

- l'API locale : accès depuis une page web, une autre application ou un autre compte Windows, contournement du jeton de session, de l'en-tête `Host` ou du contrôle des origines ;
- la fenêtre de l'application : chargement d'un contenu tiers, ouverture de liens, accès aux permissions du navigateur ;
- le stockage des clés d'API d'IA et des données de l'utilisateur ;
- le traitement des offres importées, qui sont du contenu non fiable : injection de script, injection d'instructions dans l'IA, récupération d'une adresse qui viserait la machine ou le réseau interne ;
- la sauvegarde, l'export et l'import des données ;
- l'installeur.
