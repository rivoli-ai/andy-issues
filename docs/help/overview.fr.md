---
title: Présentation d'Andy Issues
slug: andy-issues-overview
order: 1
tags: [issues, stories, backlog]
---

# Présentation d'Andy Issues

Andy Issues possède le suivi des problèmes, l'affinement des stories par dépôt et la gestion d'arriéré pour l'écosystème Andy. C'est le foyer canonique pour les stories que Conductor expose dans l'onglet Stories et qu'il transmet aux exécutions d'agents.

## Ce qu'il fait

- Stocke les stories à la portée d'un dépôt — titre, corps, statut, priorité, étiquettes et historique d'affinement.
- Affine les stories avec des suggestions assistées par IA (critères d'acceptation, plan de test, cas limites) appelables depuis l'UI Conductor.
- Suit les relations de blocage pour que Conductor puisse calculer les vagues d'exécution depuis le graphe de dépendances.
- Publie les événements de changement sur NATS pour que l'onglet Stories de Conductor se mette à jour en direct sans sondage.
- Agit comme stockage à long terme pour les stories qui proviennent d'en dehors de Conductor (synchronisation GitHub, Azure DevOps).

## Concepts clés

- **Story** — l'unité de travail qu'une exécution d'agent peut consommer. Possède des champs structurés plus un corps libre.
- **Affinement** — une passe IA qui propose des critères d'acceptation + un plan de test. L'utilisateur garde, édite ou rejette.
- **Portée de dépôt** — chaque story appartient à exactement un dépôt. Le travail inter-dépôts est modélisé comme des stories liées.

## Où il s'intègre

L'onglet Stories de Conductor est un client mince au-dessus d'Andy Issues. Les exécutions d'agents consomment les stories comme entrée principale. Dépend d'Auth, RBAC, Settings et Code Index (pour l'affinement qui doit grep le code source).

## Configuration

Les défauts de modèle d'affinement, les intervalles de sondage et le vocabulaire d'étiquettes résident sous `andy.issues.*` dans `andy-settings`. Les surcharges par dépôt résident dans `.andy/issues.yml` dans le dépôt.

## Dépannage

- **Liste de stories vide après synchronisation** — le jeton du fournisseur a expiré ou la portée est mauvaise. Reliez le fournisseur dans **Réglages → Connexions**.
- **L'affinement expire** — le fournisseur de modèle est lent ou en panne. Essayez un modèle différent dans **Réglages → Modèles** et réessayez.
- **Mises à jour en direct manquantes** — la connexion NATS s'est rompue ; redémarrez le service et l'onglet Conductor se rattachera.
