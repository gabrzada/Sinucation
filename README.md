# FriendSlop — Caçapa Russa

Jogo de sinuca multiplayer feito em Unity, com partidas entre jogadores e bots, efeitos de caos e personalização.

## Abrir o projeto

1. Instale o [Git LFS](https://git-lfs.com/) e execute `git lfs install`.
2. Clone o repositório e, se necessário, execute `git lfs pull` na pasta clonada.
3. No Unity Hub, adicione a pasta do repositório e abra com **Unity 6000.7.0b2**. A primeira abertura importa os assets e resolve os pacotes; pode levar algum tempo e requer acesso à internet.
4. Abra `Assets/_Project/Scenes/Game.unity` e pressione Play.

## Organização

- `Assets/_Project/`: cenas, código, recursos e testes do jogo.
- `Packages/` e `ProjectSettings/`: dependências e configurações do Unity.
- `Blender/`: ferramentas e materiais de produção dos modelos.
- `Docs/`: decisões técnicas, notas e documentação do projeto.

Para executar os testes, abra **Window > General > Test Runner** no Unity e escolha EditMode ou PlayMode. As cenas incluídas na build estão em Build Settings.

Arquivos gerados pelo Unity, builds locais e caches não são versionados. Assets binários usam Git LFS conforme `.gitattributes`. Alguns pacotes são obtidos pela internet; dependências de terceiros e suas licenças devem ser verificadas antes de redistribuir o jogo.
