# Le format de l'import CSV des offres

- **Mis à jour le** : 2026-11-24, avec #40
- **Décision** : [ADR-0015](adr/0015-import-csv-des-offres.md)

L'écran « Importer des offres », ouvert depuis la liste des offres, lit un fichier CSV d'offres. Sollicito montre d'abord ce qu'il ferait de chaque ligne, dans un aperçu qui n'écrit rien ; il importe ensuite, en une seule fois, les offres que l'utilisateur confirme. Ce document est la référence du format ; l'écran en reprend l'essentiel.

## Un exemple

Données fictives :

```text
poste;entreprise;lieu;publieeLe;adresse;texte
Comptable;Lumière Verte SA;Liège;18/11/2026;https://emploi.exemple.invalid/offres/1;"Première ligne du texte
Deuxième ligne, avec un ; et des ""guillemets"""
Chauffeur;Brasserie Lune Rousse;Namur;2026-11-10;;
Magasinier;;Huy;;;
```

## Les colonnes

La première ligne, l'en-tête, nomme les colonnes, dans l'ordre qu'on veut. Les noms se lisent sans tenir compte de la casse ni des blancs du début et de la fin.

| Colonne | Obligatoire | Règle |
| --- | --- | --- |
| `poste` | oui | un texte d'une ligne, de 200 caractères au plus, avec au moins une lettre ou un chiffre |
| `entreprise` | non | un texte d'une ligne, de 200 caractères au plus, avec au moins une lettre ou un chiffre ; l'entreprise de même clé est reprise, sous son nom enregistré, sinon elle se crée |
| `lieu` | non | un texte d'une ligne, de 200 caractères au plus |
| `publieeLe` | non | `JJ/MM/AAAA` (le jour d'abord, sur un ou deux chiffres comme le mois) ou `AAAA-MM-JJ` ; du 1er janvier 2000 à aujourd'hui |
| `adresse` | non | une adresse web complète, qui commence par `http://` ou `https://`, sans nom d'utilisateur ni mot de passe, de 2 000 caractères au plus |
| `texte` | non | le texte de l'offre, sur plusieurs lignes, de 50 000 caractères au plus ; gardé tel quel, fins de ligne ramenées à un saut de ligne |

- Une colonne absente vaut une cellule vide sur chaque ligne. Une cellule vide, ou faite de blancs, ne fait rien.
- Un texte d'une ligne refuse les sauts de ligne, les tabulations et les autres caractères de contrôle ou invalides ; le texte admet les sauts de ligne et les tabulations.
- Rien n'est interprété : une cellule qui commence par `=`, `+`, `-` ou `@` reste du texte, et le HTML s'affiche tel quel, comme du texte.

## L'encodage, le séparateur, les guillemets

- **UTF-8**, avec ou sans BOM. Dans Excel : « Enregistrer sous », au format « CSV UTF-8 ». Tout autre encodage est refusé ; un fichier sans aucun accent passe toujours.
- **Le séparateur** : `;` ou `,`, lu sur l'en-tête. L'en-tête n'en porte qu'un des deux.
- **Les guillemets** droits (`"`) entourent une cellule qui contient le séparateur, un guillemet ou un saut de ligne ; un guillemet s'y écrit `""`. Hors d'une cellule entre guillemets, un guillemet est une erreur.
- **Les fins de ligne** : CRLF, LF ou CR seul ; la dernière est facultative.

## Les lignes

- **Le numéro d'une ligne** est celui d'un tableur : l'en-tête est la ligne 1, et une cellule sur plusieurs lignes ne décale pas la suite.
- **Une ligne vide**, ou dont toutes les cellules sont vides, est ignorée et comptée.
- **Une ligne invalide** n'est pas importée : une cellule refusée par sa règle, ou une ligne qui n'a pas autant de cellules que l'en-tête. L'aperçu donne son numéro et ses motifs, jamais son contenu.

## Les bornes

Au-delà d'une borne, le fichier est refusé en entier, et rien n'est importé :

- 2 Mo au plus (2 097 152 octets) ;
- 1 000 lignes au plus sous l'en-tête, lignes vides comprises ;
- pour chaque cellule, la borne de son champ : 200 caractères pour le poste, l'entreprise et le lieu, 2 000 pour l'adresse, 50 000 pour le texte.

Un fichier mal formé (un guillemet mal placé, ou jamais fermé), un en-tête qui ne nomme pas les colonnes du format, ou un fichier qui ne porte aucune offre, sont refusés de même.

## Les doublons

L'aperçu dit le sort de chaque ligne ; l'import le refait, et fait foi si la base a changé entre-temps.

- **Déjà enregistrée** : une offre importée d'un fichier a les mêmes champs, aux blancs, aux majuscules et à la forme de la date près. La ligne est écartée. Réimporter un fichier complété n'importe donc que ses lignes nouvelles.
- **En double** : une ligne précédente du fichier a les mêmes champs. La ligne est écartée.
- **Doublon possible** : une offre enregistrée, ou une ligne précédente du fichier, a la même entreprise et le même poste, d'où qu'elle vienne. La ligne ne s'importe que si l'utilisateur la garde, dans l'aperçu. Jamais de fusion.

## Ce qui ne sort pas de la machine

La fenêtre de Sollicito lit le fichier et en envoie les octets à l'API locale, sur l'ordinateur ; ni son nom ni son chemin ne partent. Les offres importées vont dans la base, comme les offres collées. Sollicito ne lit jamais l'adresse d'une annonce.
