# Personagens — refinamento de 26/09/2026

## Resultado

- Corpo compartilhado: tronco com contorno suave, suspensórios contínuos sobre os ombros, fechos circulares, emblema em base costurada, cinto e bolsas laterais.
- Mangas com acabamento na cor do jogador, joelheiras claras e botas com biqueira arredondada, sola em camadas, cadarços e tira colorida.
- Luva esquerda: corrigida a orientação das faces e normais. A malha antiga estava invertida no Unity; as posições dos vértices e o pivô foram preservados.

O asset integrado é `Assets/_Project/Resources/Models/FS/character.bytes`. Os três arquétipos usam as mesmas peças. A altura, os comprimentos do esqueleto e o código da tacada permanecem os da versão atual do jogo.

## Arquivos

- `characters.blend`: três personagens montados, com coleções separadas, peças de origem em `SOURCE - joint-local meshes` e iluminação de estúdio. Ative a coleção SOURCE para editar as peças em coordenadas da articulação.
- `lineup.png`, `square-detail.png`, `back.png`: renders Blender. A iluminação é de apresentação; as cores em jogo vêm do Unity.
- `../review/after/lineup-unity.png`: captura dos três personagens feita pelo Unity com as malhas e materiais reais.
- `../review/before/` e `../review/after/`: comparação de frente, costas, três quartos e três momentos da tacada.
- `validation.json`: integridade do binário, contagem de geometria e verificações da correção da luva.

As cabeças, olhos e o encaixe dos bonés no Blender foram extraídos do Unity pelo utilitário `CharacterReview`, incluindo as normais e as transformações. As peças do corpo são montadas com os pivôs atuais do jogo. O `.blend` contém malhas separadas para edição; a animação procedural continua no Unity.

## Reproduzir

Execute a partir da raiz do projeto Unity, no PowerShell:

```powershell
& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' -b --factory-startup -P Blender/character_refine.py
Copy-Item -LiteralPath Blender/refined/character.bytes -Destination Assets/_Project/Resources/Models/FS/character.bytes
```

A fonte reproduzível desta revisão é `Blender/character_refine.py`. Editar o `.blend` manualmente não atualiza o asset do Unity automaticamente. O script usa o binário arquivado e o arquivo de cabeças em `Blender/revisions/2026-09-26-before-refinement/`.

Para gerar as capturas do Unity, feche o editor desse projeto e execute:

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.7.0b2\Editor\Unity.exe' -batchmode -projectPath "$PWD" -executeMethod FriendSlop.EditorTools.CharacterReview.Capture -characterReview Blender/review/after -logFile "$PWD/Logs/character-after.log" -quit
```

O utilitário usa uma cena temporária e não salva alterações nas cenas do jogo.

## Compatibilidade e recuperação

O script experimental `Blender/character.py` já tinha outras proporções e luvas novas, mas seu export ainda não era o asset usado pelo Unity. Esta revisão parte do asset integrado e mantém esse trabalho experimental separado. O helper `fsblend.mirror_x` também foi corrigido para que as próximas exportações não invertam a luva esquerda.

O binário anterior, o helper anterior e uma cópia do script experimental estão em `Blender/revisions/2026-09-26-before-refinement/`. Para voltar apenas à aparência anterior, copie o `character.bytes` dessa pasta para `Assets/_Project/Resources/Models/FS/character.bytes`.

## Verificação

- Blender: geração completa e abertura do arquivo salvo; todas as posições/normais/UVs são finitas e todos os índices estão dentro dos limites.
- Luva esquerda: volume orientado negativo antes da correção e positivo depois; posições e UVs idênticos aos anteriores. Um novo espelhamento pelo helper também produz faces orientadas para fora.
- Unity: compilação do utilitário e capturas dos três arquétipos; revisão visual de frente, costas e três quartos, além de preparação, recuo e avanço da tacada.
- Tacada: comparação de posições e rotações das articulações nos três momentos capturados. Hashes dos arquivos de animação, esqueleto e taco registrados antes das mudanças para conferência.

Essas capturas validam as poses amostradas. Não substituem uma sessão completa de gameplay/multiplayer.
