# Máquinas de bolas — revisão de modelos

As carcaças da máquina de pastilhas e da máquina de venda de bolas são geradas por `Blender/vending_props.py` e carregadas de `Assets/_Project/Resources/Models/FS/vending.bytes`. O Unity continua responsável pelo globo transparente, pastilhas, vitrine, bolas numeradas, textos, luz e colliders. Caso as peças não existam, as carcaças procedurais anteriores são usadas como fallback.

As 26 pastilhas foram organizadas em camadas compactas no interior do globo, sem interseção entre elas. As imagens do Unity em `../Logs/VendingModelAfter/` mostram a montagem completa. A inspeção é em cena de editor e não substitui uma sessão completa de jogo.

Reprodução: `& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' -b --factory-startup -P Blender/vending_props.py`, inspecione `Blender/vending-refined/gallery.png` e copie `Blender/vending-refined/vending.bytes` para `Assets/_Project/Resources/Models/FS/vending.bytes`.
