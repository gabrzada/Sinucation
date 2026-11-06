# Taco de bilhar — revisão de modelo

O taco mantém as raízes `Shaft`, `Butt`, `Tip` e `Ring`, seus pivôs, comprimento variável e os pontos usados pela animação. As primitivas da haste e do punho recebem como filhos as peças afuniladas `Shaft` e `Butt` de `Assets/_Project/Resources/Models/FS/cue.bytes`; se não existirem, os cilindros anteriores continuam visíveis. A ponta visual é ajustada ao diâmetro menor da nova haste; seu extremo permanece no mesmo ponto de contato.

A fonte é `Blender/cue_props.py`. Para reproduzir: `& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' -b --factory-startup -P Blender/cue_props.py`, inspecione `Blender/cue-refined/gallery.png` e copie `Blender/cue-refined/cue.bytes` para `Assets/_Project/Resources/Models/FS/cue.bytes`.

As capturas `../Logs/CueModelReview/aim_59.png`, `aim_79.png` e `aim_99.png` usam a malha real com o avatar em três momentos amostrados no editor. Elas não substituem uma partida completa nem conferem todas as combinações de cosméticos.
