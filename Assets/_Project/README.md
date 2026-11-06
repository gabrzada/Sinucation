# Friend Slop — bilhar, roleta e sabotagem (primeira pessoa, multiplayer)

## Abrir e jogar
1. Menu **FriendSlop ▸ Abrir cena de jogo** (a cena v3 é gerada automaticamente; se a cena aberta for antiga usa **FriendSlop ▸ Criar ou reconstruir cena de jogo**).
2. Play → menu principal (estilo dos concepts, sobre a arena 3D):
   - **JOGAR** — treino offline com 1–5 bots (mínimo de 2 participantes: 1 humano + 1 bot, ou 2 humanos sem bots).
   - **MULTIPLAYER** — navegador de salas:
     - **Partida rápida** — entra na primeira sala pública com lugar (online, depois LAN); se não houver, cria uma.
     - **Criar sala** — nome, online (código via relay Unity) ou rede local, pública/privada, máx. jogadores (2–6), bolas (auto/3–15), debuffs e sabotagem ligados/desligados, bots iniciais (só rede local). Não há limite de rondas: a partida acaba quando sobra um jogador vivo.
     - **Salas abertas** — lista de salas públicas online + LAN (descoberta automática por broadcast). Salas privadas ("fechadas") não aparecem: entra-se com o **código da sala**.
     - Barra de baixo: código da sala + ENTRAR, ou IP/porta + LIGAR.
   - **PERSONALIZAR** — nome, forma da cabeça, cor, boné, acessório (só visual).
   - **DEFINIÇÕES** — separadores Geral / Gráficos / Áudio / Controlos / Acessibilidade.
3. **Sala de espera**: código da sala com copiar e **CONVIDAR** (copia uma mensagem pronta), placas dos jogadores (pronto / anfitrião), **REGRAS DA SALA** (anfitrião: jogadores, bolas, vidas, rack, roleta, debuffs, sabotagem, prémios, custos, tempos, bots e dificuldade dos bots, expulsar), **PRONTO** (R) e **COMEÇAR**. **Tab** liberta o rato.
   - Salas online precisam do projecto ligado a um projecto Unity Cloud (*Project Settings ▸ Services*).

## Controlos
| Acção | Tecla |
|---|---|
| Andar / olhar / saltar / correr | WASD / rato / Espaço / Shift |
| Interagir (máquina de sabotagem, jukebox) | **E** (Q/E/Esc sai) |
| Pegar no taco (na tua vez) | **E** |
| Recuar o taco e tacar | segurar clique, puxar o rato para trás, soltar |
| Mira fina / efeito / vista de cima / largar taco | Shift / WASD / Tab / Q |
| Puxar o gatilho (roleta) | **segurar Espaço** (ou clique) ~0,75 s |
| Emotes / chat / atirar objecto | 1–4 / T / G |
| Marcar um sítio (todos vêem; recarga validada pelo anfitrião) | **botão do meio** do rato |
| Ouvir o YouTube que a sala está a tocar / reactivar o som | **J** |
| Fim da partida: placar ↔ modo livre (o vencedor dispara, clique esq.) | **Tab** |
| Pausa | Esc |

## Economia e sabotagem
- Vencedor da ronda **+100**, restantes **+50**, e **+10 por cada bola legal encaçapada**. Saldo máximo 300.
- A loja é a **máquina de sabotagem** física: na janela de sabotagem vai até ela, carrega **E** — a câmara faz zoom ao ecrã e escolhes o alvo:
  - **Travessura** (60): Curva à direita/esquerda, Bola Pesada, Mira Tremida, Taco Ensaboado.
  - **Caos** (120): Ecrã Invertido, Modo Aranha, Bêbado, Visão de Túnel, Miopia e Daltonismo.
  - Repetir o mesmo alvo custa +25%; quem vai 2+ rondas atrás do líder tem −25%. Uma compra por janela.
- A máquina mostra sempre os preços reais (Travessura e Caos), o teu saldo e se dá para pagar; o anfitrião cobra exactamente isso, uma vez por pedido.
- A roleta: a máquina atira o revólver a quem foi sorteado, que o leva à têmpora; o gatilho só responde depois disso. Sair o disparo = **gosma vermelha** estilizada (sem gore) e eliminação; sobreviver = debuff aleatório (carta na HUD com nome, categoria, descrição e tacadas restantes).
- Faltas (branca encaçapada, bola errada primeiro, sem contacto) dão **bola na mão** ao seguinte. Não há "bola fora da mesa": a física é plana e uma bola que passe a linha das tabelas só o faz pela boca de uma caçapa, e conta como encaçapada (ver `Docs/DECISOES.md`).

## Gráficos
Predefinições Baixo/Médio/Alto/Ultra e cada opção do mínimo ao máximo: resolução, modo de ecrã, VSync, limite de FPS, texturas, filtro anisotrópico (até 16×), sombras, efeitos, pós-processamento (ACES, bloom, cor, vinheta, grão, aberração), anti-aliasing (FXAA/SMAA/MSAA/TAA), oclusão ambiente (SSAO), profundidade de campo, motion blur, escala de render com upscaler **FSR 1.0** ou **STP**.
**DLSS**: o Unity 6.7 tem o código (IUpscaler/DLSS no SRP Core), mas está atrás do "upscaler framework" que ainda não está activo nesta beta — a opção aparece automaticamente quando o motor a expuser.

## Música (jukebox)
Jukebox na arena com fila partilhada (pedir faixa, votar para saltar; sincronizada pelo tempo do servidor), biblioteca CC0 e player oficial YouTube já integrado. Investigação de Spotify, Deezer, Apple Music e proposta de fallback autorizado: `Docs/MUSIC_PROVIDERS.md`.

## Créditos de assets
- Efeitos sonoros: packs CC0 do Kenney.nl — ver `Resources/Audio/CREDITS.txt`.
- Música: faixas CC0 do OpenGameArt (autores na jukebox e em `CREDITS.txt`).
- Fontes: Anton e Oswald (SIL Open Font License, licenças em `Resources/Fonts`).

## Testar multiplayer
- **Window ▸ Multiplayer ▸ Multiplayer Play Mode**: activa 1–3 jogadores virtuais; num cria sala em rede local, nos outros usa a lista de salas ou IP `127.0.0.1`.
- Builds com piloto automático (joga, tira screenshots e escreve relatório):
  - host: `FriendSlop.exe -fsHost 7790 -fsBots 1 -fsStartWhen 4 -fsRounds 3 -fsAutoplay -fsReport host.json -fsShots shots -fsMachineShot`
  - cliente: `FriendSlop.exe -fsJoin 127.0.0.1:7790 -fsAutoplay -fsReport c1.json -fsShots shots_c1 -fsMenuShots`
  - `-fsPractice 3` treino offline; `-fsBalls N` fixa as bolas; `-fsMenuShots` fotografa os ecrãs do menu; `-fsQuit 600` fecha ao fim de N segundos.

## Arquitectura (resumo)
- `Scripts/Match/MatchDirector` — autoridade única: turnos, regras, ronda, sorteio, roleta, economia, sabotagem por níveis, abandono. C# puro, testado em EditMode (43 testes).
- `Scripts/Billiards` — física própria de passo fixo (determinística); os clientes só recebem snapshots.
- `Scripts/Net/NetGame` — listen server (Netcode for GameObjects): sala com lugares reservados para reconexão, bots, snapshots a 10 Hz (`MatchView`), bolas a 30 Hz com interpolação, jukebox, eventos e RPCs com remetente validado.
- `Scripts/Presentation` — primeira pessoa, arena industrial (`RoomBuilder`), máquinas físicas (`SabotageMachine`, `RandomizerMachine`, `JukeboxStation` via `InteractionSystem`), personagens chibi (`AvatarVisual`), público, gosma (`GooFX`), gráficos (`GraphicsQuality`), sessões online/LAN (`SessionService`, `LanDiscovery`) e HUD IMGUI (`HudView*.cs`). Nada aqui decide regras.
- Dados editáveis em `Data/` (MatchConfig e modificadores; novos efeitos são acrescentados sem alterar os índices anteriores).
