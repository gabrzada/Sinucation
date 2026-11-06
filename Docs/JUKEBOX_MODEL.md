# Jukebox — revisão de modelo

A jukebox usa as peças `JukeboxCabinet` e `JukeboxRecords` de `Assets/_Project/Resources/Models/FS/jukebox.bytes`, geradas em `Blender/jukebox_props.py`. A carcaça conserva o arco, com molduras e discos na vitrine. O Unity continua a montar a luz da vitrine, botões, grelha, texto, néon e lâmpadas. Se qualquer peça estiver ausente, a parte correspondente usa as primitivas anteriores.

Áudio, player online, ponto de interação, seleção e collider continuam sob `JukeboxStation`. A comparação visual antes/depois foi feita em `../Logs/JukeboxBefore/jukebox.png` e `../Logs/JukeboxAfter/jukebox.png`, no editor; isso não constitui um teste de reprodução online real.

Reprodução: `& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' -b --factory-startup -P Blender/jukebox_props.py`, inspecione `Blender/jukebox-refined/gallery.png` e copie `Blender/jukebox-refined/jukebox.bytes` para `Assets/_Project/Resources/Models/FS/jukebox.bytes`.
