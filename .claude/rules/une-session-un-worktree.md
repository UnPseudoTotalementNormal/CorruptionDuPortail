# Une session, un worktree

Plusieurs sessions travaillent ici en même temps : chacune dans son worktree, jamais dans le dossier principal, qui ne reçoit
que la branche principale (hook `.claude/tools/worktree_guard.py`). Sans ça, un `git add` embarque les lignes d'une autre session et
un commit casse le build des autres.

- Commencer : `python .claude/tools/session_worktree.py new <nom>` depuis le dossier principal, puis travailler dans le dossier
  donné, en chemins absolus. Les dépôts que le projet ignore y sont aussi, à la même place.
- Finir : `python <dossier principal>/.claude/tools/session_worktree.py land`, lancé depuis le worktree. Tout doit être commité ; un
  conflit n'intègre rien et dit quoi faire.
- Rien à nettoyer en fin de tâche : un ménage passe en fond (après chaque `new`, à chaque début de session) et retire les worktrees
  fusionnés, même sur GitHub, vide au bout de 24 h d'inactivité les dossiers qui se refont seuls (`Library/`, `node_modules/`…), retire
  au bout de 14 jours un worktree propre en gardant sa branche. Le travail pas commité n'est jamais touché. Ce qu'il a fait :
  `.git/session-worktree.log` ; ce qu'il ferait : `session_worktree.py clean --dry-run`.
- Worktree retiré alors qu'on y revient : `python <dossier principal>/.claude/tools/session_worktree.py revive <nom>` le refait depuis sa
  branche.
- `git stash` jamais : sa pile est commune à toutes les sessions et à tous les worktrees.
- Ce qui se lance depuis le dossier principal (un service, un serveur) ne voit le travail qu'une fois intégré.

## Ici : Corruption du Portail

- Le dossier principal reste sur `Dev` : c'est là qu'est l'éditeur visuel de Poyo. Une session n'y écrit pas de code ; elle pilote
  son propre Unity headless dans son worktree (`tools/HEADLESS_UNITY.md`, `--project-path <worktree>`). `new` recopie le
  `Library/` du dossier principal (robocopy, sans `ScriptAssemblies`, `Bee`, `Pipeline` ni verrous, comme `unityctl.sh unpark`) :
  la première ouverture ne réimporte pas tout.
- Finir : comme avant, branche poussée, PR vers `Dev`, `gh pr merge --squash` (le ménage reconnaît une branche squashée), puis
  `git pull --ff-only` dans le dossier principal. `land` seulement pour ce qui va droit sur `Dev` sans PR (skills, outils).
- Le ménage vide le `Library/` d'un worktree inactif depuis 24 h (`unityctl.sh park` fait pareil à la main) : la prochaine
  ouverture le recopie avec `unityctl.sh unpark`. Un Unity ouvert sur le worktree le tient : rien n'est vidé tant qu'il tourne.
- `CorruptionDuPortail-ci` (à côté du projet) est le worktree des builds de `tools/build/local_ci.py` : hors `.claude/worktrees/`,
  le ménage n'y touche pas.
