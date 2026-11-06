# Objetos industriais — revisão de modelos

A arena usa cinco peças próprias em `Assets/_Project/Resources/Models/FS/industrial.bytes`: dois contêineres, baú, cone e placa de aviso. O contêiner tem painéis, molduras e travas; o baú usa o mesmo acabamento dos caixotes; a sinalização segue as cores da arena. A fonte é `Blender/industrial_props.py`.

O carregador `ArenaProps` substitui apenas os caminhos usados pelo cenário e conserva o fallback para os FBX anteriores. As medidas das malhas correspondem às dimensões normalizadas dos FBX, preservando o espaço dos colliders e o empilhamento dos contêineres. As capturas em `../Logs/IndustrialModelAfter/` mostram o resultado no editor Unity; o baú fica parcialmente oculto por um pilar e um barril na posição atual do cenário.

Para reproduzir: `& 'C:\Program Files\Blender Foundation\Blender 5.2\blender.exe' -b --factory-startup -P Blender/industrial_props.py`, inspecionar `Blender/industrial-refined/gallery.png` e copiar `Blender/industrial-refined/industrial.bytes` para `Assets/_Project/Resources/Models/FS/industrial.bytes`. A inspeção no editor não substitui partida completa ou medição de desempenho.
