# Decisões de design e técnicas — ronda de correcções de 2026-09-29

## Bola fora da mesa (regra retirada)
**Investigação.** A física do jogo é 2D (plano do pano): as bolas nunca saltam, e as tabelas são segmentos contínuos
com pontas arredondadas junto às caçapas. A regra "falta: bola fora da mesa" só disparava num caso: uma bola que
entrava pela boca de uma caçapa, entre a ponta da tabela e o círculo de captura, e passava a linha das tabelas sem
tocar no círculo. Isso não é uma bola a sair da mesa — é uma bola a entrar na caçapa por um "buraco" da geometria.

**Decisão.** Não se introduzem saltos artificiais. `BilliardsSimulation.PocketAt` passa a considerar encaçapada
qualquer bola cujo centro passe a linha das tabelas (só é possível pela boca de uma caçapa), na caçapa mais próxima.
A falta `BallOffTable`, o campo `ShotRecord.OffTable`, os avisos da HUD e as referências nas instruções foram
retirados. Faltas que existem: branca encaçapada, bola errada primeiro, nenhum contacto — todas dão bola na mão ao
jogador seguinte, em qualquer sítio da mesa. Testes: `RoundOfFixesTests.BallThroughTheCornerJawIsPocketedNeverOffTable`
e `HardShotsNeverLeaveTheTable`.

## Colisões e previsão da mira
As colisões bola-bola e bola-tabela recuam até ao instante exacto do toque (tempo de impacto), resolvem e avançam o
resto do passo. Antes, a bola penetrava até ~1,5 cm por passo nas tacadas fortes e a normal do choque (e portanto a
saída da bola tocada) desviava da linha prevista. A previsão (`PredictCue`) usa o mesmo integrador (atrito, curva dos
debuffs, tabelas com efeito lateral, caçapas) até ao primeiro toque, por isso é exacta quando não há erro aleatório;
com o "Taco Ensaboado" mostra um cone em vez de uma linha, para não prometer uma precisão que a simulação não tem.

## Bola na mão
Cliente e anfitrião usam a mesma validação (`BilliardsSimulation.DescribePlacement`): área de jogo, atrás da linha de
cabeceira na abertura, sem sobreposição. A pré-visualização encaixa no sítio válido mais próximo; a confirmação é um
pedido com resposta do anfitrião (aceite/recusado com motivo) e a mira só começa depois de aceite. Só o jogador da vez,
só na fase de tacada (física parada) e com limite de frequência.

## Jukebox do YouTube partilhada
O estado (faixa, fila, pausa, posição pela hora do servidor) já era replicado; a música não tocava nos outros porque o
player oficial só era criado para quem tinha aceite o aviso no painel da jukebox. Agora aparece a todos um aviso
"A SALA ESTÁ A OUVIR — [J] OUVIR TAMBÉM" (o player liga-se aos serviços da Google, por isso continua a ser uma escolha
explícita, lembrada nas definições de Áudio). Se o navegador bloquear o som, o mesmo botão volta a pedir a reprodução.
O player já não recomeça a faixa quando acaba antes do anfitrião avançar a fila.

## Participantes
Mínimo de 2 participantes (humanos + bots): 1 humano precisa de pelo menos 1 bot; 2 humanos jogam sem bots.

## Escala da interface
80–135 %, com limite automático para a área virtual nunca ficar abaixo de 1180 × 760 (em 4:3/5:4 ou janelas baixas a
escala máxima efectiva é menor, e as definições dizem-no).

# Jukebox do YouTube: página oficial num navegador embutido (2026-10-24)

## Porquê
O "mini player" era o player incorporado (IFrame API) numa página local servida em 127.0.0.1, dentro do WebView2.
Muitas músicas de editoras recusam esse contexto com o erro 150 (101/153), mesmo com `embeddable=true` na Data API:
nos logs havia 74 vídeos diferentes com erro 150. Teste A/B no mesmo WebView2 e na mesma rede (24/10): **embed 0/74,
página oficial 73/74** (o que falta pede login por idade/verificação). Vídeos que já tocavam continuam a tocar.

## Como funciona
- `OnlineMusicPlayer` abre `youtube.com/watch?v=ID&t=Ns` (um carregamento por faixa; desvios corrigem-se com `seekTo`,
  nunca a recarregar). `StreamingAssets/Jukebox/watch.js` só usa a API do player da própria página (`movie_player`):
  volume, pausa, posição, estado, erros (`playabilityStatus`) e o rectângulo do player.
- Máscara: a página não é alterada (nada escondido no DOM). O painel recorta a textura à zona do player e desenha-a no
  mesmo sítio e tamanho do mini player antigo. Sem player (escolha de cookies do YouTube) mostra a página inteira.
- Cliques e roda do rato no vídeo vão para a página (saltar anúncio quando o YouTube deixar, cookies, avisos);
  teclado nunca. Anúncios correm como no site: sem saltos de posição nem silêncio forçado; chip "ANÚNCIO" no painel.
- Plugin nativo (`Blender/third-party/unity-webview/.../WebViewPlugin.cpp`, recompilado): `SetURLPattern` passou a
  funcionar; navegação fora do YouTube (login da Google, anúncios) é cancelada e, se veio de um clique, abre no
  navegador do sistema; nunca abre janelas pop-up por cima do jogo; falhas de rede chegam como `CallOnError`.
- Erros: login (1001) / indisponível (1002) → o anfitrião procura outra versão (como antes com o 150); removido (100)
  → salta; aviso de conteúdo (1003) → o jogador confirma no vídeo; rede → novas tentativas locais. Botão
  "ABRIR NO YOUTUBE" sempre disponível (posição da sala, navegador do sistema), também sem WebView2.
- O catálogo deixou de descartar vídeos "não incorporáveis" (Data API) e aceita o 401 do oEmbed (título provisório
  `OnlineTrack.PlaceholderTitle`, corrigido pela página ao tocar via `ReportOnlineMusicMeta`). ProtocolVersion 11.
- Playlists sem chave nem feed: `YouTubePlaylistProbe` cria um player IFrame mudo só para ler a lista e destrói-o logo.

## Custos e limites
- WebView2 do jogo: ~+240–345 MB de memória face ao embed; CPU semelhante (~2–3 % da máquina de 16 núcleos).
- Anúncios no início da faixa: cada jogador avisa o anfitrião enquanto a página carrega ou mostra um anúncio
  (`ReportOnlineMusicBusy`); a sala espera pelo último (`OnlineMusicState.AdHold`, relógio parado no 0:00, máximo
  45 s, uma vez por faixa) e todos começam a música do início ao mesmo tempo. Quem já está pronto vê "à espera dos
  anúncios dos outros". Testado com anfitrião + cliente (os dois com anúncio de ~25 s): arrancaram juntos no 0:00.
- [J] silencia o anúncio (o som volta sozinho quando a música começa) e [K] carrega no "Pular" do próprio YouTube
  quando ele já deixa (`button.ytp-skip-ad-button`). Ambos são acções do jogador: o jogo nunca silencia, pula,
  recarrega para fugir a anúncios nem os bloqueia — isso violaria os Termos do YouTube.
- Sem login dentro do jogo (a Google não o permite em navegadores embutidos): o que exige conta abre no YouTube.

# Painel do anfitrião, turnos, caçapas e debuffs (2026-10-24)

## Painel do anfitrião
Menu de pausa → "PAINEL DO ANFITRIÃO" (também a meio da partida). Separador REGRAS: as mesmas regras do lobby; durante
a partida ficam pendentes e entram em vigor no início da próxima ronda (`MatchDirector.RoundStarting` →
`NetGame.ApplyPendingRules`), por isso a ronda em curso nunca muda. Separador SALA: subir à mesa (vale logo), jukebox
(adicionar e pular, independentes) e "REINICIAR PARTIDA" com confirmação (`HostResetMatch`: ronda 1, saldos, roleta e
efeitos do zero, mesmos jogadores, sem esperar pelo "pronto").

## Subir à mesa
`LobbySettings.AllowTableClimb` (por omissão desligado = como antes). Desliga a `PlayerTableBarrier` (só a camada
Player colide com ela, por isso objectos atirados passam sempre) e deixa de empurrar para fora quem está em cima do
tampo. Ao voltar a desligar, quem está em cima é empurrado para a borda.

## Tempo por tacada
20 s por omissão (código, `Data/MatchConfig.asset` e lobby); o anfitrião muda-o nas regras (0 = sem limite). O
cronómetro já vinha no snapshot; nos últimos 5 s fica vermelho e a pulsar.

## Jukebox: adicionar e pular
`OnlineMusicState.SkipLocked` (comando `SkipLock`), separado de `Locked` (adicionar). Bloqueado: só o anfitrião pula
ou volta músicas (YouTube e rádio, incluindo os votos); o servidor recusa os outros com aviso, e eles vêem ANTERIOR/
PRÓXIMA desactivados com o motivo.

## Indicadores
Anel brilhante e seta a flutuar sobre a bola legal (só com as bolas paradas); coral e a pulsar na última bola. Painel
de turnos: quem joga (avatar + nome), cronómetro grande, "A SEGUIR", bola legal e a bola que ganha a ronda, e na
última bola o aviso com a condição (tocar-lhe primeiro e metê-la). Tudo vem do snapshot do anfitrião.

## Caçapas
Antes: a bola caía sempre que o centro passava a linha da tabela numa abertura (laterais com 15,6 cm de abertura),
sem bocas — ângulo e velocidade não contavam. Agora cada caçapa tem duas bocas (jaws, `TableGeometry.Jaws`) que
convergem para uma garganta mais estreita, com ressalto mais "morto" (0,55); a bola só cai depois de passar a garganta,
e a base inclinada da caçapa (`ShelfAcceleration`, só fora da linha das tabelas) leva para dentro uma bola lenta que já
passou o nariz. As bocas desenham-se na mesa. Afinado com um varrimento da física real (branca sozinha, ±8 cm de
desvio em passos de 2,5 mm), largura efectiva de entrada directa:

| Caso | Antes | Agora |
|---|---|---|
| Canto de frente | 8,8 cm | 8,3–8,8 cm (limite: entrada do modelo 3D) |
| Canto a 30° | 7,8 cm | 7,3 cm |
| Canto a 40° (junto à tabela) | 6,0 cm | 5,5 cm — bolas a rolar coladas à tabela entram a 1–4 m/s |
| Lateral de frente | 11,8 cm | 5,3–6,3 cm |
| Lateral a 45° | 7,5 cm | 2,5 cm |
| Lateral a 60° | 4,5 cm | 0 (como numa mesa real) |

Jogadores simulados (600 tacadas por nível: bola aleatória, caçapa com menor corte, mira pela bola fantasma com erro
gaussiano, mesmas posições nas duas físicas): bom (0,4°) ~58% → 47%, médio (0,8°) ~40% → 32%; canto e lateral
equilibrados (antes 52% vs 73%, agora 46% vs 51%). Bocas mais fechadas (canto 28°, lateral 45°) davam 40%/29% — difícil
demais para um jogo de festa; laterais a 40° voltam a 72% (há um "precipício" entre 40° e 42°, por isso 43°).

Parâmetros em `BilliardsPhysicsSettings` (bocas de canto 34°/5 cm, laterais 43°/4,5 cm). A entrada do canto continua
a do modelo (12,9 cm entre narizes, tipo mesa de bar); estreitá-la exige mudar `Blender/table.py`.

## Debuffs acumulativos
Causa: todos os efeitos de câmara estavam no mesmo grupo de conflito (`StrongCamera`, política Replace) — Miopia
depois de Aranha removia a Aranha — e o limite era 2 por jogador. O jogo usa os assets em `Data/Modifiers` (e o código
para Miopia/Daltonismo), por isso mudaram os dois. Agora cada efeito tem o seu estado e as suas tacadas; repetir o
mesmo renova só o dele (Refresh); limite 4. Incompatibilidades explícitas: Aranha × Invertido (ambos rodam a câmara)
e Curva Esquerda × Direita — o mais recente substitui. A composição visual já era por componentes separados (rotação,
desfoque, mistura de canais, vinheta) e passou a receber vários efeitos.

# TV e leitor de DVD ligados à jukebox (2026-11-01)

## Uma fila, três sítios
A TV de parede que já existia no lounge (reaproveitada: o `TvStation` desenha por cima do ecrã dela) e o novo leitor
de DVD por baixo (`Presentation/MediaCenter.cs`, DVD e móvel em `Blender/media_props.py`) não têm estado próprio: usam o `OnlineMusicState` da jukebox (fila, faixa, pausa, relógio, bloqueios), autoritativo no
anfitrião e enviado só quando muda. O que se põe no DVD aparece no painel da jukebox e vice-versa; pausa, retoma,
anterior/próxima, remover e limpar valem para os dois. Cada item guarda só onde foi escolhido (`OnlineTrack.Device`,
"via DVD"/"via jukebox", na fila, no toast e na TV). Permissões iguais às da jukebox (só anfitrião, adicionar,
pular). ProtocolVersion 12.

- **DVD [E]**: o mesmo painel da jukebox em modo DVD (abre no separador YouTube; "pôr no DVD").
- **TV [E] "ver na TV"**: a câmara fica em frente ao ecrã, com uma barra de controlos (anterior/pausa/próxima,
  volume local, [J]/YouTube) e os cliques no vídeo vão para a página oficial (pular anúncio quando o YouTube deixar,
  escolha de cookies, avisos).

## Imagem na TV
A TV mostra o fotograma do mesmo navegador embutido da jukebox (página oficial `youtube.com/watch`, recortada ao player
como no painel). Nenhum vídeo passa pela rede do jogo: cada jogador vê a sua página, sincronizada pelo relógio da sala
(desvios corrigidos com `seekTo` pela `watch.js`); quem entra a meio recebe o estado ao ligar-se e abre na posição da
sala. A cópia da textura só corre com a TV à vista e a menos de 14 m (ou o painel aberto); de perto pede a página à
largura do ecrã para ficar nítida.

Sem imagem, a TV não finge um videoclipe:
- **Rádio CC0**: cartão "RÁDIO CAÇAPA · SÓ ÁUDIO" (faixa, autor, "faixa CC0 sem vídeo", progresso, equalizador
  decorativo parado em pausa).
- **YouTube sem consentimento**: título, quem pediu e "[J] VER E OUVIR AQUI" (sem miniatura: a miniatura também é um
  pedido aos servidores da Google, por isso só depois do consentimento).
- **A carregar / anúncios dos outros / falha**: miniatura escurecida e o estado do player deste jogador (falha do
  WebView2, rede, vídeo que pede login → "ABRIR NO YOUTUBE no DVD"). Anúncio: a TV mostra o anúncio (é o que a
  página mostra), com o chip "ANÚNCIO"; a sala espera como antes (`AdHold`, máx. 45 s).
- **Nada na fila**: "CAÇAPA TV — escolhe um vídeo no leitor de DVD".

## Som
A TV também é coluna. YouTube: o volume local usa a coluna mais audível (jukebox ou TV, com a mesma regra de
distância e paredes). Rádio: uma segunda fonte na TV segue a da jukebox (mesma faixa, posição a menos de 40 ms,
pausa e volume). Volume e mudo continuam locais ("só tu").

## Validação (2026-11-01, build Windows, 3 processos na mesma máquina)
Anfitrião + cliente + um terceiro cliente que entra 75 s depois (`-fsMediaTest host|client|late`, AutoPilot.Media.cs;
vídeos: Big Buck Bunny e Sintel, filmes abertos da Blender Foundation):
- Anfitrião põe A na jukebox, cliente põe B no DVD: os três vêem a mesma fila com "via jukebox"/"via DVD".
- Pausa na TV do cliente → todos em pausa no mesmo ponto (40,0 s); retoma na jukebox do anfitrião; PRÓXIMA no DVD do
  cliente; ANTERIOR na TV do anfitrião; limpar + próxima → rádio CC0 e cartão "só áudio" em todas as TVs.
- Desvio página↔sala (amostras a cada 3 s): normalmente −0,05…−0,3 s; depois de retomar −0,4…−0,6 s. Início de cada
  faixa: a sala espera 3–6 s pelo carregamento de todos (`AdHold`) e arrancam juntos no 0:00.
- Entrada a meio: o terceiro entrou com A a meio; −2,4 s na primeira leitura, −0,1 s a seguir.
- Falha local: o WebView2 do cliente "fecha" (`SimulateFailure`) → a TV dele mostra o cartão com o erro, a sala segue
  (anfitrião e terceiro sem mudança); [J] de novo → página reaberta na posição da sala e de volta a −0,1…−0,3 s.
- Correcção de desvio na `watch.js`: na 2.ª corrida o cliente recuperado ficou preso a −2,1 s (o limiar era 2 s e um
  vídeo 4K/60 demora ~2 s a retomar depois de cada `seekTo`, por isso cada salto voltava a deixá-lo atrás). Agora o
  limiar é 1 s (no máximo a cada 6 s) e a página aprende quanto demora a retomar e salta essa margem à frente (0–3 s,
  medida 3 s depois de cada correcção). Vale também para a jukebox.
- Link inválido no DVD → aviso "Nenhum vídeo disponível" só para quem o pôs; fila intacta. Sem excepções nos logs.

Por verificar: anúncios reais na TV (nesta corrida o YouTube não mostrou nenhum; o caminho é o mesmo do painel, já
testado a 25/10), escolha de cookies e cliques reencaminhados a partir da TV, máquinas diferentes / internet real,
relay online, som de ouvido, custo de desempenho da cópia da textura, e uso durante uma partida a decorrer.

# Relatos: painel do anfitrião, volume do vídeo, tiro e ragdoll (2026-11-02)

Cada relato foi reproduzido antes de corrigir. A validação foi feita em builds Windows (`Builds/GoalValidation`) com
vários processos na mesma máquina. Os laboratórios do AutoPilot têm uma opção que repõe o comportamento antigo na
mesma build (`-fsFunLegacy`, `-fsVolumeLegacy`), para o antes/depois.

## Painel do anfitrião
- **Causa:** `HudView.Update` fechava as regras da sala em todos os frames durante a partida (`if (view.InMatch)
  _roomSettingsOpen = false`). Era um resto de antes do painel existir. Pausa → "PAINEL DO ANFITRIÃO" fechava a pausa e
  o painel desaparecia no frame seguinte. Reproduzido com cliques reais num treino local: 0,2 s depois já não havia
  painel. A QA de 24/10 passava porque chamava `HostApplySettings` directamente, sem passar pela UI.
- **Correcção:** as regras só se fecham ao *entrar* na partida. O botão usa `HudView.OpenHostPanel()`. "+ BOT"/"− BOT"
  ficam desactivados a meio da partida (o anfitrião já os recusava sem aviso).
- **Validação:**
  - Cliques reais na build final: o painel abre a meio da partida e fica aberto. SUBIR À MESA liga/desliga, TEMPO POR
    TACADA 20 → 25 s, e os botões de bots aparecem desactivados.
  - `-fsPanelTest` com anfitrião + cliente: 11/11 PASS. Inclui a nova verificação (painel aberto pela UI continua aberto
    186 frames depois) e as permissões do cliente: pular bloqueado, subir à mesa e debuffs compostos.

## Volume do vídeo com a jukebox a 100 %
- **Medição** (`-fsVolumeLab`, 4 vídeos: Big Buck Bunny, Sintel, Rick Astley, Despacito):
  - Painel da jukebox, TV, mesa e canto da sala, a cada segundo. Regista o volume enviado e as parcelas, o volume do
    player e do `<video>` na página, e o loudness do vídeo.
  - Em paralelo, um medidor de áudio do Windows por sessão (picos do WebView2 e do jogo).
- **Causa principal: curva de distância.** Com o mesmo controlo "volume da jukebox · só tu", o vídeo e a rádio da
  jukebox tinham curvas diferentes.
  - Rádio: AudioSource 55 % 3D, linear de 4 a 30 m, sem abafar.
  - Vídeo: `2,5/d` e ×0,55 com paredes.
  - Resultado junto à mesa (4,9 m), com jukebox 100 % e geral 0,90: a rádio tocava a ~0,7 e o vídeo a 46 (≈25 com
    paredes).
  - No medidor do Windows: rádio com pico ~0,7, vídeo com pico ~0,25–0,37.
- **A TV como coluna não entra na causa.** Vale a coluna mais audível, por isso a TV só sobe o volume perto dela (1,3 m
  → 100 %).
- **Normalização do YouTube** (factor do próprio site, não do jogo): em vídeos altos o `<video>` fica abaixo do volume
  pedido.
  - Despacito, +5,7 dB: player 90, `<video>` 0,47.
  - Rick Astley, +1,0 dB: `<video>` 0,80.
  - Vídeos baixos (−2,0 e −4,7 dB) não são subidos.
  - É o mesmo que no youtube.com e não se mexe (a página não é alterada). Com a curva antiga, isto somado à distância
    deixava o Despacito a 0,24 junto à mesa.
- **Correcção:** o vídeo usa a mesma curva da rádio (`OnlineMusicPlayer.DistanceGain`): cheio até 4 m, ~87 % a 10 m,
  nunca abaixo de 45 % na sala, ×0,8 com paredes.
- **Resultado junto à mesa:** o jogo manda 88 em vez de 46. Pico no Windows: 0,27 → 0,47 (Big Buck Bunny) e 0,24 →
  0,41 (Sintel).
- **Diagnóstico que fica:** a `watch.js` diz o volume do player, o do `<video>` e o loudness. O log `[Jukebox] página`
  mostra as parcelas.

## Revólver do vencedor visto pelos outros
- **Causa:** `RiskDeviceView.FunAim` usava `NetPlayer.Yaw`, que só o dono actualiza. Nos outros clientes ficava no
  valor do teletransporte e o braço apontava para um rumo fixo do mundo, de lado ou para trás (o que se vê em
  `referencias/relato-tiro/frente.png` e `lado.png`).
- **Medição** (`-fsFunLab`, anfitrião = alvo, cliente 1 = vencedor, cliente 2 = observador): cano a −90° do corpo nos
  dois remotos e a 0° no dono.
- **Correcção:** `NetPlayer.BodyYaw` (no remoto, a rotação do NetworkTransform).
- **Resultado:** cano a +0° em todos os processos, em todas as corridas.

## Ragdoll na vista de quem leva o tiro e dos outros
- **Causa, ao levantar-se:**
  - O dono teletransporta-se para onde a bacia caiu.
  - A mistura de recuperação partia da pose local, por isso o corpo saltava outra vez o deslocamento: bacia 0,92–0,94 m
    num frame, em todos os processos.
  - Com "Movimento da câmara" Completo, a câmara do atingido ia do chão para os olhos de uma vez (1,0 m).
  - Nos modos confortáveis a câmara não mostrava nada e, no fim, saltava ~0,94 m.
- **Correcções** (`AvatarRagdoll`, `FirstPersonRig`, `NetPlayer`):
  - A recuperação parte da pose caída no mundo.
  - Nos outros clientes o boneco fica no chão até chegar a posição nova do dono (no máximo +0,5 s) e só então se
    levanta, já no sítio certo.
  - A câmara do atingido mistura-se com o corpo enquanto ele se levanta:
    - Completo: vai com a cabeça e roda.
    - Reduzido (omissão): desce a direito até ~0,5 m, sem rodar, e volta a subir.
    - Nenhum: fica onde está.
  - Os olhos deslizam (~0,4 s) até ao sítio novo.
  - O próprio corpo fica escondido enquanto cai.
  - Aviso "Levaste um tiro de X! No chão por um segundo."
- **Validação:** 3 processos, dois tiros ao anfitrião e um ao observador, nos 3 modos de câmara.
  - Bacia: maior passo por frame ≤ 0,08 m em todas as vistas (antes 0,92–0,94; o que fica é a própria queda).
  - Câmara do atingido: ≤ 0,13 m/frame em Completo, ≤ 0,19 em Reduzido (início da queda), ≤ 0,18 em Nenhum (deslize),
    contra 1,0 m antes.
  - Corpo próprio visível em 0 frames.
  - Numa corrida houve paragens de 0,3–0,55 s depois do primeiro tiro nos três processos. Não se repetiram nas três
    corridas seguintes da mesma versão: atribuído à contenção de 3 jogos na mesma GPU, não confirmado.
- **Testes EditMode:** `ReportedIssuesTests` (curva igual à da rádio; bacia sem salto quando o dono se muda para onde
  caiu, e o salto antigo reproduzido).

## Por verificar
- Máquinas diferentes / internet real.
- Pessoas a jogar: o vencedor a rodar o rato enquanto dispara.
- Bots vencedores (usam outra posição).
- Som de ouvido. O medidor do Windows mede picos, não loudness percebido.
- Anúncios durante o laboratório de volume.

# Bola presa na boca da caçapa (2026-11-02)

Relato: "uma pequena parte da bola parece impedir que ela entre na caçapa". A imagem do relato não está no projecto;
o defeito foi reproduzido sem ela. Provas, harnesses e medições em `Logs/PocketMouth/`.

## Reprodução
- **Em jogo:** `-fsPractice 1 -fsStartWhen 2 -fsPocketLab <pasta>` (`AutoPilot.PocketLab.cs`). No turno do jogador
  local, lança bolas lentas para a boca de um canto e de uma lateral, com a física autoritativa e a apresentação
  reais, e fotografa cada caso de dois pontos de vista. Antes da correcção, os 4 lançamentos lentos ficaram parados
  na boca: no canto a 2,4 e 6,5 cm do centro da caçapa, na lateral a 3,5 e 5,7 cm. Nas imagens
  (`Logs/PocketMouth/before/`) a bola aparece "cortada" pela parede preta do copo da caçapa, que sobe à frente da boca.
- **Na simulação:** harness `Stuck`, ~96 000 lançamentos lentos (−40° a +40°, ±3 cm, 0,40–0,95 m/s). No canto,
  595 bolas paravam com o centro já depois da garganta. Outras 42 ficavam congeladas na base inclinada, e a tacada só
  acabava no limite de 25 s.

## Causas (modelo e física)
1. **Modelo da mesa.** O forro escuro de cada caçapa (cilindro em `Blender/table.py`) subia até 5 cm acima do pano.
   O fundo escuro (disco de 5,8/6,6 cm de raio) ficava pousado no pano, até 2,4 cm para dentro da linha das tabelas.
   Uma bola que entra, ou que pára junto ao nariz, atravessa essa parede preta: é a "pequena parte" que parece
   segurá-la. O aro de aço já tinha sido recortado assim (`PocketRimModelTests`); o forro e o fundo não.
2. **Cantinho do pano depois da garganta.** Nas caçapas de canto, a garganta (linha entre as pontas das bocas)
   atravessa o canto da área de jogo. `PocketAt` só deixa cair a bola depois de o centro sair do pano, e a base
   inclinada só puxava fora do pano. Uma bola lenta que passava a garganta e parava nesse cantinho (≈2,2 × 2,2 cm)
   ficava lá para sempre, com o centro por cima do buraco.
3. **Base inclinada vs. `StopSpeed`.** Fora do pano a base puxa 1,2 m/s², mais do que o atrito de rolamento. Mas a
   integração zerava a velocidade abaixo de `StopSpeed` em cada passo, antes de a inclinação actuar. Uma bola que
   chegava quase parada congelava ali, e a simulação só terminava aos 25 s.

`BallInterpolator` e a animação de queda (`TableView.AnimatePocketing`) não são causa: a queda só começa quando o
anfitrião encaçapa a bola.

## Correcção
- **Modelo (`Blender/table.py`).** O forro e o fundo escuros são recortados como o aro: nada escuro fica acima do pano
  dentro da linha das tabelas. O corte começa 2 mm abaixo do pano, para a face cortada ficar escondida.
  - `table.bytes` foi regenerado. Antes, confirmou-se que o script sem alterações reproduz a geometria actual.
  - Comparado com o anterior, só mudaram triângulos do material `dark`. Aro, tabelas, placas, corpo e `Cushions`
    ficaram iguais.
  - Cópia do anterior em `Blender/revisions/2026-11-02-before-pocket-liner/`.
- **Física (`BilliardsSimulation`).**
  - A base inclinada passa a actuar também no cantinho do pano depois da garganta. Aí puxa para o centro da caçapa,
    e a bola sai do pano e cai.
  - Na base inclinada, a bola já não pára abaixo de `StopSpeed`.
- **O que se mantém.** A bola só cai depois de passar a garganta e sair do pano. As bocas ficam a 34°/43°. Antes da
  garganta, a base só actua fora do pano. Uma bola parada entre os narizes e a garganta, com o centro ainda no pano
  verde, continua "pendurada" na boca, como numa mesa real. Agora vê-se inteira, à beira do copo
  (`Logs/PocketMouth/after/`).
- **Rejeitado: pôr a base inclinada em toda a boca, desde a linha dos narizes.** Corrigia o mesmo defeito, mas mudava
  outras jogadas. Bolas que tremiam entre as bocas e paravam junto a uma delas, longe do buraco, passavam a rolar para
  dentro. O canto a 30° passava de 8,0 para 11,0 cm, e o jogador bom de 41,0 % para 47,3 %. Isso desfazia o
  equilíbrio afinado em 2026-10-24.

## Medições (antes → depois)
Mesmos harnesses e sementes; código em `Logs/PocketMouth/harness/`. Os harnesses de 2026-10-24 já não existiam e
foram refeitos; os números absolutos não são comparáveis com os dessa data.

| Medição | Antes | Depois |
|---|---|---|
| Jogador bom (σ 0,4°, 0,8–3 m/s, 600 tacadas) | 41,0 % (canto 38,6 %, lateral 64,3 %) | igual |
| Jogador médio (σ 0,8°) | 24,0 % (canto 22,2 %, lateral 41,1 %) | igual |
| Toque suave (0,45–1 m/s) | 2,7 % | 2,8 % (+1 tacada) |
| Tacadas que acabam no limite de 25 s (1 800) | 1 | 0 |
| Largura efectiva (canto 0°/30°/40°, lateral 0°/45°/60°; 1, 2 e 4 m/s) | — | igual em todos os casos |
| Bolas a rolar coladas à tabela (0–10 mm, 0,3–4 m/s): canto e ao longo da lateral | — | iguais |
| Lentas paradas depois da garganta (canto) | 595 | 0 |
| Lentas congeladas na base, física > 10 s | 42 | 0 |
| Lentas paradas com o centro sobre o escuro do modelo novo | 42 | 0 |

- **Em jogo** (build `Builds/GoalValidation`, mesmo laboratório):
  - O caso do canto depois da garganta agora cai.
  - Os dois casos "pendurados" (canto antes da garganta, lateral antes do nariz) ficam parados como antes, mas a bola
    vê-se inteira sobre o pano.
  - As entradas a 1,5 m/s caem como antes.
- **Testes EditMode:** `PocketMouthStuckTests` (6 testes): o caso reproduzido, varrimento de bolas lentas, cantinho
  depois da garganta, base sem congelar, jogadas de controlo e o forro fora do pano. Resultado: 272/281; só falham as
  9 `CueGripTests.RightGloveWrapsTheCueDuringStroke`, que já falhavam antes.

## Por verificar
- A queda não foi vista quadro a quadro; só se viu o antes e o depois de cada lançamento.
- Partidas em rede: a correcção é só do anfitrião e do modelo, e os clientes recebem o resultado.
- Os narizes e as bocas desenhadas (`TableView.BuildJaws`) não mudaram.

# Pausa contra bots (2026-11-02)

Quem joga sozinho contra bots pode parar a partida e retomá-la exactamente onde estava.

## Quando existe
- Só numa partida em que o anfitrião é o único humano: os outros lugares são bots, não há mais ninguém ligado
  (nem espectadores) e não há humanos desligados com o lugar reservado. Regra em `SoloPause.Allowed`; o anfitrião
  calcula-a e envia-a no snapshot (`MatchView.PauseAllowed`).
- Vale para o treino (`StartPractice`) e para salas LAN/online em que só ficou o anfitrião com bots.
- Com 2 ou mais humanos a pausa não aparece: a tecla P não faz nada, o menu Esc diz "A partida continua: há outros
  jogadores na sala" e o anfitrião recusa um pedido de pausa directo.

## Como se usa
- **Esc:** sozinho contra bots, abrir o menu de pausa congela a partida; o título passa a dizer "PARTIDA PARADA · só tu
  contra bots" e o botão principal "RETOMAR PARTIDA". Fechar o menu (botão ou Esc) retoma.
- **Tecla P:** pausa/retoma sem abrir o menu. Aparece então o aviso "PARTIDA EM PAUSA" a meio do ecrã, com o ecrã
  escurecido e um botão RETOMAR. A tecla aparece na ajuda do ecrã (só quando existe) e em Definições ▸ Controlos.
- **Painéis abertos a partir da pausa** (Painel do anfitrião, Personalizar): a partida continua parada. Por cima
  desses painéis e do placar final, o aviso encolhe para uma etiqueta no topo, para não tapar botões.

## O que pára
- **Autoridade (`MatchDirector.Paused`):** `Tick` não avança. Ficam parados as fases (intro de ronda, resultado,
  sorteio, revelação, loja), o relógio do turno de 20 s e o do gatilho, a física das bolas e o relógio da partida
  (`MatchTime`). Esse relógio também mede as recargas da sabotagem. Tacada, bola na mão, gatilho, compras e "passar"
  são recusados ("A partida está em pausa." / "Partida em pausa"). O log da partida regista `pause`/`resume`.
- **Bots:** `BotBrain.Update` não corre e a mira fica como estava, por isso o taco não salta. Os tiros dos bots no modo
  livre (`UpdateFunBots`) e os tiros do humano (`FunShotRpc`) também param.
- **Clientes:** o snapshot leva `MatchPaused`, e o relógio local dos clientes não desconta. Isto só serve de rede de
  segurança, porque durante a pausa não há clientes. `ProtocolVersion` 12 → 13.
- **Apresentação no anfitrião:** `Time.timeScale = 0` enquanto a partida está pausada. Como o anfitrião é o único
  humano, isto só lhe afecta o ecrã. Param animações, ragdolls e objectos atirados. A rede (NGO usa tempo real), a
  jukebox/rádio, a TV, o HUD, a câmara e o AutoPilot usam tempo real e continuam. Ao retomar ou ao sair
  da sessão, a escala volta a 1.
- **Não pára:** a música (jukebox, rádio e vídeo da TV), de propósito: é da sala, não da partida.

## Alguém entra durante a pausa: recusado
**Decisão:** enquanto a partida está pausada, o anfitrião recusa qualquer ligação nova. O motivo mostrado a quem tenta
entrar é: "A partida está em pausa: o anfitrião está a jogar sozinho contra bots. Tenta entrar quando ele a retomar."
O anfitrião recebe um aviso com o nome: "X tentou entrar, mas a partida está em pausa. Retoma (P) para deixar entrar."

Porquê recusar e não retomar sozinho:
- Quem pausa normalmente afastou-se do jogo. Retomar automaticamente punha a correr, contra uma pessoa ausente, o turno
  de 20 s (perde a vez), a loja, os bots e a roleta. A roleta é o caso grave: o gatilho dispara sozinho ao fim do
  tempo e pode eliminar quem está ausente.
- Quem entra a meio de uma partida só fica a ver (espectador). Recusar não lhe tira lugar nenhum. Pode tentar outra
  vez depois de o anfitrião retomar, e o anfitrião sabe que alguém quer entrar.
- A regra "a pausa só existe com um humano" fica sempre verdadeira. Não há um estado "pausado com dois humanos" para
  resolver.

Detalhes:
- A recusa acontece na aprovação da ligação (`GameBootstrap.Approve` → `NetGame.JoinRefusal`).
- Quem foi aprovado mesmo antes da pausa e acaba de ligar durante ela é desligado com o mesmo motivo
  (`RefuseJoinWhilePaused`).
- Rede de segurança: se, por qualquer razão, a sala deixar de ser "um humano contra bots" com a partida pausada, a
  partida retoma e todos recebem um aviso (`NetGame.UpdatePause`).
- **Reconexão:** um humano desligado com o lugar reservado impede a pausa, por isso não pode "voltar durante a pausa".
  Quem já foi eliminado por abandono deixa de contar. Se tentar voltar durante a pausa, é recusado como os outros.

## Validação (2026-11-02)
- **Testes EditMode:** `SoloPauseTests` (16 testes). Cobrem a intro de ronda, o relógio do turno com tacada e bola na
  mão recusadas, bolas a rolar paradas ao milímetro que depois continuam, revólver (nem o bot dispara nem o tempo
  acaba; depois o bot puxa o gatilho), loja (relógio, compras e "passar"), recargas da sabotagem, mira do bot
  congelada, modo livre, snapshot e quem pode pausar (6 casos). Resultado: 288/297. Só falham as 9
  `CueGripTests.RightGloveWrapsTheCueDuringStroke`, que já falhavam antes.
- **Em jogo:** build `Builds/GoalValidation`, laboratório `-fsPauseLab` (`AutoPilot.PauseLab.cs`). Em cada cenário
  pausa pelo caminho real (tecla P, Esc ou botão), espera 4 s, confere e retoma. Provas (logs e capturas) em
  `Logs/SoloPause/`; o script está em `Logs/SoloPause/pauselab.ps1`.
  - Cenários: intro de ronda, mira do humano, mira do bot, bolas a rolar, sorteio, gatilho, revelação, recarga da
    sabotagem, loja (`-fsShopPhase`) e modo livre. O modo livre foi testado a meio do ragdoll, com vencedor humano e
    com vencedor bot.
  - Em cada cenário confere-se: fase e relógio iguais (no anfitrião e no HUD), tempo de jogo parado, bolas paradas
    (0,000 mm), nenhuma tacada, ronda ou gatilho, mira do bot igual, recargas paradas, bonecos e pose parados, nenhum
    tiro do modo livre, `timeScale` 0, aviso ou menu no ecrã, e compras/tacadas/gatilho recusados. Depois de retomar:
    o relógio continua do mesmo valor (ex.: gatilho 9,99 → 8,80 s em 1,2 s), as bolas voltam a rolar (0,7–1,2 m), as
    recargas descem (20,00 → 18,80 s) e o ragdoll continua (≈0,65 m).
  - Corridas finais: solo 140 PASS, loja 131 PASS, entrada durante a pausa 146 + 2 PASS, dois humanos 3 PASS.
    0 FAIL.
  - **Entrada durante a pausa** (anfitrião LAN + 2 bots, cliente "Visitante"): o cliente é recusado com o motivo
    claro, o anfitrião vê o aviso com o nome e a pausa mantém-se com 1 ligação. Depois de retomar, o cliente entra
    como espectador; a pausa fica escondida e P, menu e RPC directo não pausam. Quando ele sai, a pausa volta.
  - **Dois humanos sentados:** `PauseAllowed` falso, a tecla P não pausa, e o menu e o pedido directo não congelam a
    partida. O menu diz "A partida continua".
- **Um frame de atraso no ecrã:** a autoridade pára no próprio frame do pedido. O `timeScale` só tem efeito no frame
  seguinte, porque o `deltaTime` de um frame já está decidido quando ele começa. Medido quadro a quadro: os bonecos
  andam esse único frame (até 5 cm, com um bot a andar e um frame de 28 ms) e depois nada se mexe. É a mesma latência
  de qualquer tecla.

## Por verificar
- Não foi testado com cliques reais de rato nem com teclas físicas. As teclas P/Esc foram injectadas no Input System
  e o botão RETOMAR foi chamado pelo mesmo método. O AutoPilot não aceita cliques reais no IMGUI.
- Sala online (relay): a recusa usa a mesma aprovação de ligação que a LAN, mas só se testou em LAN/local.
- O caso "aprovado mesmo antes da pausa e ligado já durante ela" (`RefuseJoinWhilePaused`) e a rede de segurança que
  retoma (`UpdatePause`) não foram provocados em jogo, porque a janela é de milissegundos. Estão só revistos no
  código.
- Pausar com a máquina de sabotagem aberta ou com o painel do anfitrião aberto não foi visto em jogo. Pelo código,
  as compras são recusadas pelo anfitrião e o aviso encolhe para uma etiqueta.
- A pausa no Editor (Play) não foi experimentada; só na build.
