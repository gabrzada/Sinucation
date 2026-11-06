# Objetos arremessáveis — revisão de modelos

A bola de papel, a lata e o patinho agora usam as peças `PaperBall`, `Can` e `Duck` de `Assets/_Project/Resources/Models/FS/throwables.bytes`, geradas por `Blender/throwables.py`. As malhas têm dimensões em metros e são instanciadas sob as raízes originais com a escala inversa dessas raízes. O patinho usa amarelo, bico laranja, asas e olhos; a lata recebe tampa e anéis; o papel tem facetas irregulares.

Apenas os renderizadores são substituídos quando a malha existe. As primitivas continuam como fallback, e os colliders, camadas, massa, velocidade, amortecimento, duração e sons continuam os mesmos. O teste `ThrowableModelTests` verifica as formas de colisão e a queda das três peças em modo de jogo. A captura com materiais do Unity está em `../Logs/throwables-unity.png`.

Para reproduzir: `& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' -b --factory-startup -P Blender/throwables.py`; inspecione `Blender/throwables-refined/gallery.png`, depois copie `Blender/throwables-refined/throwables.bytes` para `Assets/_Project/Resources/Models/FS/throwables.bytes`.
