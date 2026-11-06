# Revisão de estabilidade — 26/09/2026

## Estado

Em andamento. A aprovação final depende do teste do executável e da revisão visual.

## Implementação existente

Os três personagens usam peças rígidas ligadas a uma hierarquia de Transform e animação procedural. Não há Animator Controller, Animation Clips, skinning ou Avatar humanoide nesse caminho. As correções preservam esse sistema.

## Problemas confirmados e correções

- Torcida: a altura de mundo era descontada duas vezes no limite de alcance das pernas. O cálculo agora usa a altura local uma única vez.
- Transição da tacada: o bot abandonava a pose ao entrar na simulação. O acompanhamento mantém a postura por 0,55 s. Alvos de IK expirados não atraem mais as mãos para a origem do mundo.
- Comemoração: gestos ociosos e emotes podiam sobrescrever a pose. A comemoração tem prioridade e retorna ao Idle após 2,4 s.
- Acessórios: o rig antigo é desativado antes da destruição adiada. Materiais próprios são liberados ao reconstruir/destruir o avatar. Headwear e acessórios possuem raízes identificáveis.
- Encaixe: óculos seguem a separação dos olhos e a curvatura de cada cabeça; laço/cachecol têm ajustes por forma. Itens do tronco seguem o corpo compartilhado, que tem as mesmas medidas nos três personagens.
- Mesa: pano elevado de 0,40 m para 0,62 m; malha reconstruída no Blender e collider ajustado. Comprimento, largura, raio das bolas e geometria física no plano da mesa permanecem iguais.
- Interface: setas dos seletores usam espelhamento de UV, evitando a rotação ao redor de um pivô de tela dentro do scroll.
- Materiais: malhas importadas pelo carregador FSM recebem tangentes para normal maps.
- Estilo: original, cel suave e cel marcado; mudança global em runtime, incluindo bolas. O padrão de instalações novas é original.

## Evidência disponível

`Logs/audit-tests.xml`: 62 testes passaram. Inclui testes existentes de física, regras, comandos de rede e partidas completas simuladas, além de regressões de animação, altura e acessórios.

O teste combinatório percorre 108 combinações (3 personagens × 6 chapéus × 6 acessórios) e quatro poses. Verifica slots únicos, materiais presentes e limites finitos. Isso não substitui a inspeção visual de clipping.

## Verificação pendente

- Capturas de todos os acessórios e animações no renderer real.
- Tacadas, comemorações, roleta, debuffs, eliminações e retorno ao menu no executável.
- Três partidas consecutivas e persistência após reabrir.
- Menus/HUD em 16:9, 16:10 e 21:9; 1080p, 1440p e 4K.
- Tempo de inicialização antes/depois da otimização; desempenho dos três estilos.
- Revisão dos logs, shaders e recursos após a última alteração.

Backups anteriores às correções: `Blender/revisions/2026-09-26-before-audit`.
