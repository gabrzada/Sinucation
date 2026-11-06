# Caçapa Russa — 1.2.0-preview

Atualização de estabilidade, controles e efeitos visuais.

## Multiplayer e saldo

- Corrigido o encerramento e a troca de salas para evitar conflitos de conexão.
- Reconexão automática do cliente à mesma partida quando possível, preservando seu lugar e saldo.
- Uma sessão nova começa com zero moedas. O saldo de uma partida encerrada não pode financiar compras em outra.
- Corrigidos resíduos de saldo na interface ao trocar de sessão.
- Compras de Caos recebem confirmação do servidor; preços por alvo e motivos de bloqueio ficam mais claros.
- Fechar o jogo não inicia uma nova tentativa de conexão.

## Sinuca e controles

- Ajustadas as colisões nas entradas das caçapas e nas pontas das tabelas.
- Corrigidos encaçapamentos indevidos de bolas fora das bocas das caçapas.
- É possível ajustar a mira horizontal enquanto se puxa o taco.
- O efeito da bola agora usa **W/A/S/D** durante a preparação da tacada; combinações diagonais funcionam e **C** zera o efeito.
- Pausar cancela o recuo do taco, evitando uma tacada acidental ao voltar.
- Números das bolas maiores e reflexos menos intensos.
- Personagens não podem subir na mesa; objetos arremessados continuam colidindo com ela sem alterar as bolas de sinuca.

## Travessuras e personalização

- Visão de Túnel mais intensa.
- Novos efeitos: **Miopia** e **Daltonismo**.
- Novo cosmético: **chapéu de palha com faixa vermelha**, compatível com as três formas de cabeça.

O player YouTube existente foi preservado. Esta versão não acrescenta navegação pelo site completo nem novos serviços de música.

## Como jogar

Windows 64 bits. Extraia o ZIP inteiro para uma pasta e execute **CacapaRussa.exe**, mantendo a pasta `CacapaRussa_Data` e as DLLs ao lado do executável.

Todos os participantes devem atualizar para **1.2.0-preview** (protocolo multiplayer 10). A partida continua dependendo do anfitrião; sua perda definitiva não tem migração automática de host.
