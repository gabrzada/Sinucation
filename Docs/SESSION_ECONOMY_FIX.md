# Saldo entre sessões — correção adicional

O saldo de compras pertence à partida. Uma nova sessão começa com zero moedas; reconectar à mesma partida ativa recupera o saldo mantido pelo servidor.

Foram reproduzidas duas falhas em testes antes da correção:

- O objeto de rede mantinha a referência ao diretor/economia e o snapshot anterior após `OnNetworkDespawn`.
- A HUD conservava a carteira animada da sessão anterior e mostrava esse valor ao vincular outra sessão.

O construtor de uma nova partida já criava jogadores com saldo zero. A correção atua no descarte de estado da conexão e na apresentação: ao encerrar ou reinicializar o peer, são invalidados diretor, jogadores, snapshots, loja e dados da mesa. A HUD reinicia a carteira ao vincular outra sessão ou trocar de contexto de partida. O servidor não apaga a carteira de um participante que apenas desconecta e volta à mesma partida.

Validação:

- Testes antes da alteração: duas falhas reproduzidas e um teste de isolamento do construtor aprovado.
- Depois da alteração: **56 testes direcionados passaram**, incluindo os três testes de regressão novos.
- Executável: partida com 240 moedas, falha de conexão do anfitrião, nova sessão no mesmo NetworkManager e na mesma porta. Nova identidade de partida, saldo autoritativo e replicado em zero, compra de Caos recusada por saldo insuficiente. **Zero falhas**.
- Dois executáveis: cliente com 240 moedas, queda provocada e reconexão à mesma partida. Mesmo jogador e saldo de 240 restaurados. **Zero falhas**.
- Build Windows atualizada com sucesso em `Builds/GoalValidation/FriendSlop.exe`.

Evidências em `C:\Users\lucas\Desktop\FriendSlop\Logs`: `session-economy-before.xml`, `session-economy-after.xml`, `session-economy-build.log`, `economy-isolation.log`, `economy-reconnect-client.log`, `SessionEconomyIsolation/result.json`, `SessionEconomyReconnect/result.json` e `session-economy-validation.json`.

Histórico de vitórias, personalização e preferências normais não foram apagados. A validação nova foi feita em LAN local; as evidências de Relay da rodada anterior permanecem no relatório principal.
