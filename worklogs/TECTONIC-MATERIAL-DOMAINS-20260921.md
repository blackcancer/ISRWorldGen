# Domaines tectoniques transportés — candidat de reprise

Base : cf9bb4288f455594e34888bdc2bd8d5df03ffe08. Périmètre : Core/Evolution, programme de campagne, export et workflow tectonic-history. Registre, API du jeu, sauvegardes, autres campagnes et transport donneur conservés.

## Défaut visé
La réattribution au site Voronoï mobile le plus proche ne transportait pas l'identité avec la croûte. Le candidat initialise une fois des coordonnées de plaques puis transporte leurs mesures positives avec les mêmes faces donneur que la croûte et son moment d'âge. Les vitesses locales sont la moyenne des vitesses prescrites pondérée par ces coordonnées. Les normales de contact viennent du gradient du contraste transporté, pas des sites déplacés.

Ce sont des coordonnées matérielles planes, PAS un inventaire de croûte survivante par plaque. Le flux basal de croûte et les sources magmatiques restent distincts ; la subduction consomme de la croûte mais pas ce traceur de coordonnées. Pas de plaques rigides, équilibre des forces, flexure ou arc. Aucun relief ajouté comme texture.

Les mesures sont conservées sans renormalisation après transport. Leur lecture en fractions divise par la somme locale. Le choix de propriétaire part du maximum global puis utilise une résolution de 1e-12 et les IDs canoniques ; cette résolution ne modifie aucun inventaire. La politique de polarité be40b872 reste intacte.

## Comparaison
AdvectPlateDomains est explicite et false par défaut dans le Core. La campagne accepte material/reference ; la CI calcule les deux sur trois seeds, 512² cellules et l'atlas entier de 1 000 000 unités. Les autres tailles restent des changements d'échelle sans recadrage. Les matériaux initiaux sont identiques. Seize nouveaux contrôles complètent les 27 existants : flux nul, translations, métrique rectangulaire, ordre, immutabilité, CFL, bilans, normales, histoire, répétabilité et échelle. La comparaison Windows/Linux conserve ses seuils et exige des PNG16 identiques.

## Publication et récupération
Commit enfant de la tête vérifiée, jamais force-push. Les sorties restent neuves et liées à leur commit. Une erreur exige sa régression, pas une tolérance élargie ni un masque d'eau. Inspection statique locale effectuée ; .NET absent localement, résultats C# à lire dans la CI du commit publié. Revue par un second agent, solution native, jeu, MCP et érosion : NOT_RUN. Aucun statut de lot promu et aucune acceptation géographique déduite des tests.
