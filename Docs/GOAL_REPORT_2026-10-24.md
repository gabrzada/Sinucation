# Rodada de correções — 24/10/2026

Correção adicional do saldo após encerramento por erro de conexão: [isolamento de moedas entre sessões](SESSION_ECONOMY_FIX.md). A build Windows foi atualizada também com esse ajuste.

## Alterações

| Pedido | Resultado |
| --- | --- |
| Compras de Caos | O cabeçalho anunciava capacidade de pagamento pelo preço base, mesmo quando repetição/desconto alteravam a cotação do alvo. Agora identifica o preço base e cada botão mantém cotação e motivo de bloqueio. O servidor responde à compra; som de sucesso e confirmação dependem de aceitação. O snapshot é atualizado após o pedido. Débito, aplicação, imunidade e idempotência continuam autoritativos. |
| Caçapas e física | As pontas físicas das tabelas agora correspondem ao recuo de 16 mm dos modelos. A regra que encaçapava qualquer posição fora do retângulo foi restringida às bocas das caçapas. Estados realmente inválidos usam a recuperação de física existente. Preservados integrador determinístico, atrito, colisões, restituição e spin; não foram alteradas constantes sem evidência de defeito. |
| Multiplayer | Sessões online encerram primeiro pelo SDK (`ISession.LeaveAsync`), que é dono do transporte; o jogo aguarda o desligamento antes de iniciar outra conexão. Operações de sessão são serializadas, falhas limpam a conexão, o fechamento do aplicativo cancela tentativas de recuperação, callbacks de encerramento esperado não derrubam a nova tela e consultas/entrada compartilham a tarefa de autenticação. |
| Reconexão | Clientes podem tentar recuperar conexão três vezes, reutilizando a identidade e o lugar reservado de 90 segundos. LAN reconecta ao endereço anterior; online tenta reentrada pelo ID via SDK. O callback de desconexão controla a transição de tela; o loop não descarta o contexto antecipadamente. Diagnósticos automáticos do NGO são distinguidos das recusas explícitas. Não há migração da simulação autoritativa quando o anfitrião desaparece. Expulsão/recusa explícita não inicia recuperação automática. |
| Travessuras | Túnel com pequena abertura central e periferia opaca; Miopia com desfoque por profundidade; Daltonismo com transformação de deuteranopia baseada em Machado et al. Os novos efeitos usam catálogo, conflito, duração de três tacadas e snapshots existentes. Efeitos de jogabilidade funcionam independentemente da opção gráfica de pós-processamento. |
| Bolas | Círculos e números maiores nas texturas, mipmaps dos números preservados em qualidade baixa e reflexos de verniz reduzidos. Mantidas cores padrão e distinção entre lisas/listradas. |
| Mesa e arremessáveis | Camadas próprias para personagens, barreira, arremessáveis e bolas. A barreira bloqueia personagens e não bloqueia objetos. A simulação das bolas permanece independente de PhysX; arremessáveis também ignoram a camada das bolas. Consultas de câmera, marcações, arma e oclusão de música ignoram a barreira invisível. |
| Chapéu | Novo chapéu de palha de aba larga e faixa vermelha, no sistema de personalização existente, encaixado nas três cabeças e ligado ao slot animado. Geometria visual sem collider. |
| Mira | Rotação horizontal continua durante o recuo. Direção é atualizada antes do envio da tacada, incluindo o frame em que o clique é solto. Movimento da personagem continua bloqueado. Perda de contexto de input cancela o recuo. |
| WASD | W/S controlam efeito superior/inferior, A/D o lateral; combinações diagonais e C para zerar. Ajuda e definições atualizadas. |
| Música | Player YouTube preservado. Pesquisa, requisitos e proposta de busca/fallback em `MUSIC_PROVIDERS.md`; nenhuma integração sem autorização foi ativada. A única alteração no player foi excluir a nova barreira invisível da consulta de oclusão. |

O protocolo de entrada mudou de 9 para 10: todos os participantes devem usar a mesma build. Novas definições são acrescentadas ao catálogo sem deslocar os índices anteriores. O projeto já tinha muitas alterações e arquivos não rastreados antes desta rodada; não foi feito reset, commit ou reconstrução da cena para substituí-los.

## Evidências e testes

- Suíte inicial: 123/123 testes EditMode passaram.
- Suíte ampliada: 139/139 passaram após correção de uma falha detectada na configuração de mipmaps. O Unity exige configurar essa opção no importador para texturas sem leitura em CPU.
- Dezesseis casos novos cobrem a classificação de quedas/recusas, saldo exato com repetição/desconto, compra única, serialização de saldo/efeito, conflitos e expiração dos três efeitos, seis caçapas em três potências, falsos encaçapamentos e matriz de colisão.
- O probe de runtime usa o RPC real de compra e eventos reais do Input System. Sua primeira execução confirmou Caos com saldo exato, mira horizontal durante o recuo, WASD diagonal, bloqueio de caminhada, cancelamento, ativação/remoção dos efeitos e colisão de objetos com a mesa sem mover bolas.
- Uma partida entre dois executáveis terminou com JSONs idênticos de vencedor, ranking, moedas, jogadores e posições/estado das bolas. Isso valida sincronização final nessa condição local, não todas as condições de rede.
- Uma partida online real pelo Unity Relay também terminou com JSONs idênticos nos dois processos: 5 tacadas, uma ronda, mesmo vencedor e estado final. A partida LAN inicial teve 10 tacadas. Os processos rodaram na mesma máquina; Relay não foi substituído por ligação direta nesse teste online.
- Revisadas as capturas reais de câmera de Miopia, Daltonismo, Túnel, estado restaurado e chapéu nas três formas de cabeça. O probe confirmou também que a direção do frame de soltura chega à simulação autoritativa. Resultado: zero falhas nesse probe.
- A validação final da build, reconexão, capturas e serialização foi registrada nos arquivos de QA indicados abaixo.
- Build Windows final concluída com sucesso (`goal-shutdown-build.log`). O probe final terminou com zero falhas, incluindo pausa/cancelamento do recuo e ausência de disparo ao retornar da pausa. Um teste posterior de fechamento de anfitrião/cliente confirmou saída completa sem iniciar reconexão durante o encerramento (`quit-host.log`, `quit-client.log`).
- Queda provocada no cliente: recuperação na primeira tentativa em LAN e em Unity Relay, preservando o jogador 1 na ronda em andamento. Em ambos os testes o cliente remoto comprou Caos com saldo exato de 120 e recebeu aceitação de custo 120.
- Cinco partidas completas comparadas: LAN inicial (10 tacadas), Relay inicial (5), LAN após queda/compra (16, duas sabotagens) Relay após queda/compra (11, duas sabotagens) e uma nova sessão LAN completa (9 tacadas). Os JSONs do anfitrião e do cliente são idênticos em cada uma. Resumo automatizado em `goal-network-summary.json`.
- Após a partida LAN de recuperação, ambos voltaram ao menu com zero avatares restantes, reconectaram e iniciaram outra partida. A segunda sessão também terminou com resultados idênticos e valida a reutilização do NetworkManager.

Resumo consolidado dos critérios de aceitação: `goal-validation-summary.json`.

Arquivos de evidência ficam em `C:\Users\lucas\Desktop\FriendSlop\Logs`. Os testes de runtime são opt-in pelos argumentos `-fs*` e usam o namespace de preferências de QA já existente, separado das preferências normais.

| Evidência | Arquivo |
| --- | --- |
| Suíte inicial | `goal-baseline.xml` |
| Suíte ampliada | `goal-acceptance-tests.xml` (139/139) |
| LAN inicial | `goal-host-1.json`, `goal-client-1.json` |
| Relay real | `online-host.json`, `online-client.json` e respectivos logs |
| Probe de input, compra, efeitos, pausa e colisões | `release-probe.log`, `GoalQA-Release/result.json` |
| Capturas dos efeitos e chapéu | `GoalQA-Release/*.png` |
| Compra remota e recuperação LAN | `release-host-1.json`, `release-client-1.json`, `release-host-2.json`, `release-client-2.json` e respectivos logs |
| Compra remota e recuperação Relay | `relay-recovery-host.json`, `relay-recovery-client.json` e respectivos logs |
| Comparação dos cinco pares de resultados | `goal-network-summary.json` |
| Build Windows final | `goal-shutdown-build.log`; executável em `Builds/GoalValidation/FriendSlop.exe` |

Reprodução: o método de editor `FriendSlop.EditorTools.GameplayGoalBuild.Build` gera a build Windows. O argumento `-fsGoalProbe <pasta>` executa as verificações de input/efeitos/colisão em uma sala de treino; `-fsDropAfter 3` provoca uma perda de transporte no cliente durante uma partida para testar recuperação. Os argumentos `-fsOnlineHost <arquivo-código>` e `-fsOnlineJoin <código>` usam o SDK oficial para testar Relay. As execuções ocultas não são benchmark de FPS.

## Limitações

- A autoridade da partida continua no anfitrião: sua perda definitiva encerra a simulação. Reentrada do cliente não equivale a migração de host.
- A imagem de erro e o código revelaram um conflito real de ciclo de vida com o SDK; isso não prova que toda desconexão histórica tenha a mesma causa. Validação com pessoas em redes distintas continua necessária para avaliar perda de pacotes, NAT, suspensão e falhas prolongadas do Relay.
- As especificações fornecidas são Ryzen 5 8600G, Intel Arc B580 de 12 GB, 16 GB de RAM e Windows 11. O DxDiag aponta erro 31 em um adaptador secundário Microsoft Basic Display Adapter. Isso é uma condição do ambiente, sem evidência suficiente para atribuir a ele as falhas de rede. Nenhum driver/configuração do Windows foi alterado. A máquina de execução dos testes usa outra GPU; não há certificação de desempenho na Arc do usuário.
- As capturas de câmera fora da tela validam materiais e pós-processamento sem depender da janela de QA estar visível; não substituem uma revisão humana completa da interface e de conforto visual.
- Spotify não pode ser acrescentado ao jogo sob sua política padrão; Deezer requer confirmação atual de acesso e autorização; Apple Music requer prova de conceito e revisão dos requisitos. Ver fontes e proposta em `MUSIC_PROVIDERS.md`.
