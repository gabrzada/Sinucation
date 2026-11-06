# Leitor de DVD e móvel da TV — modelos

A TV é a de parede que já existia no lounge (moldura e ecrã do `RoomBuilder.BuildLounge`, centro a `RoomBuilder.TvCenterY` = 2,2 m): passou a ser interactiva e o `TvStation` desenha o conteúdo por cima do ecrã (camadas sem iluminação com o vídeo ou os cartões; a antiga etiqueta "CAÇAPA TV" no meio do ecrã passou para o cartão de espera).

Por baixo dela entram duas peças novas de `Assets/_Project/Resources/Models/FS/media.bytes`, geradas por `Blender/media_props.py` no estilo dos outros adereços (corpo navy, molduras mostarda, rebites de aço, cantos boleados): `TvConsole` (móvel baixo com tampo de madeira e uma fila de caixas de DVD) e `DvdPlayer` (gaveta, visor, botões). O Unity monta por cima o visor do DVD, as luzes de estado, áudio, interacção e colliders (`Presentation/MediaCenter.cs`). Se o ficheiro faltar, cada peça cai para uma caixa simples.

Medidas que o código usa (manter iguais nos dois lados): topo do móvel a 0,52 m; visor do DVD em x = +0,115, z = 0,042 (Blender); ecrã da TV 1,55 × 0,87 m com a face a 11 cm da parede. Âncora: `RoomBuilder.MediaAnchor` (x = 0,6, parede sul, de frente para o sofá).

Reprodução: `& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' -b --factory-startup -P Blender/media_props.py`, inspeccionar `Blender/media-refined/gallery.png` e `gallery_front.png` e copiar `Blender/media-refined/media.bytes` para `Assets/_Project/Resources/Models/FS/media.bytes`.
