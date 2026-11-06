# Móveis da arena — revisão de modelos

O bar e o lounge usam oito modelos próprios exportados de `Blender/lounge_props.py` para `Assets/_Project/Resources/Models/FS/lounge.bytes`: balcão, ponta de balcão, geladeira, estante, sofá, poltrona, mesa de centro e caixa de som. A paleta segue os objetos da arena, com metal azul, bordas mostarda, madeira e estofamento coral.

`ArenaProps.TrySpawn` seleciona os novos modelos para as referências de cenário existentes. Se o arquivo ou peça não estiver disponível, `PropLibrary` continua a usar o FBX anterior. As dimensões dos modelos foram ajustadas aos limites dos FBX originais normalizados pela altura; a posição e os volumes de colisão existentes são preservados. A geladeira, estante e caixas de som apontam para a sala nas posições atuais. A geometria exportada não contém os acessórios, garrafas e plantas que o Unity posiciona sobre os móveis.

Para reproduzir: `& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' -b --factory-startup -P Blender/lounge_props.py`, inspecionar `Blender/lounge-refined/gallery.png` e instalar `Blender/lounge-refined/lounge.bytes` em `Assets/_Project/Resources/Models/FS/lounge.bytes`. A galeria é uma visualização de trabalho; as capturas montadas no Unity estão em `../Logs/LoungeModelAfter/`.

A revisão visual foi feita no editor em cena construída para inspeção; não substitui uma partida completa nem medição de desempenho.
