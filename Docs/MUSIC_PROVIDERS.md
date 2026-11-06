# Música: investigação e proposta (24/10/2026)

O YouTube existente deve continuar como está. Uma API de catálogo não concede licença de reprodução de áudio dentro de um jogo. Não usar downloads, extração de áudio, proxies de conteúdo ou troca de provedor para contornar bloqueios.

| Plataforma | Busca e reprodução oficiais | Requisitos e conclusão para este jogo |
| --- | --- | --- |
| YouTube | Data API para busca; IFrame Player API para vídeo incorporado | Preservar o player atual, controles, vídeo e identificação. Erros 100/101/150 indicam conteúdo removido ou incorporação indisponível; não tentar recuperar o áudio. Autoplay também depende do navegador e de interação do usuário. |
| Spotify | Web API para metadados; Web Playback SDK para streaming | O SDK exige autorização do usuário, token com escopo adequado e conta Premium; suporte a conteúdo protegido depende do navegador/WebView. A política proíbe criar jogos. Premium não remove essa restrição. Não integrar reprodução no jogo sob os termos padrão. |
| Deezer | APIs/plugins oficiais existem, mas acesso e reprodução não são equivalentes | O portal de desenvolvedores não pôde ser consultado nesta investigação. A resposta recente na comunidade oficial orienta contato com a Deezer para acesso de terceiros. Não afirmar que novos aplicativos têm acesso ou que o SDK legado oferece streaming completo atualmente. Exige confirmação comercial/técnica direta antes de implementação. |
| Apple Music | Apple Music API para busca; MusicKit on the Web para reprodução | É um candidato para prova de conceito, não uma integração já aprovada. Exige developer token, autorização do usuário e verificação de assinatura/capacidade de reprodução. Validar DRM no WebView Windows utilizado e os direitos aplicáveis ao uso dentro de jogo. Tokens privados de desenvolvedor devem permanecer no backend. |
| Biblioteca própria licenciada | Catálogo e áudio sob controle do jogo | Caminho tecnicamente viável para fallback compartilhado: músicas próprias, CC0 ou licenciadas expressamente para o uso pretendido. O projeto já tem biblioteca local com créditos. |

Proposta de arquitetura, condicionada à autorização dos provedores:

1. Manter `OnlineMusicPlayer` e a fila YouTube como caminho padrão. Uma futura interface de provedor exporia `Search`, `CanPlay`, `Play`, `Pause`, `Seek` e eventos de indisponibilidade, sem mover regras de partida para o player.
2. A busca pode consultar apenas provedores habilitados/autorizados em paralelo, com timeout por provedor, cancelamento e limites de requisição. Unificar candidatos por ISRC quando disponível, artista, duração e versão; títulos parecidos não garantem gravações equivalentes.
3. A fila compartilhada armazena provedor, identificador oficial, versão da faixa, duração e relógio autoritativo. Cada cliente reproduz diretamente pelo SDK oficial, com sua própria autorização. Nunca retransmitir o áudio do anfitrião aos demais.
4. Antes de escolher um fallback, verificar disponibilidade no país e conta de cada participante. Autenticação, assinatura ou bloqueio regional não devem causar um ciclo infinito de tentativas. Mostrar uma ação clara para o usuário e permitir continuar a partida sem música.
5. Fallback automático só entre candidatos autorizados e reproduzíveis. Começar pela biblioteca licenciada já existente; outros provedores ficam desabilitados até aprovação e validação de ponta a ponta. Uma falha de incorporação do YouTube pode sugerir outra gravação oficialmente incorporável ou a biblioteca licenciada, sem transformar a restrição em permissão de uso.
6. Credenciais por usuário ficam locais/seguras; segredos de serviço ficam no backend. Fila e snapshots não carregam access tokens. Preservar controles e atribuições exigidos por cada SDK.

Fontes primárias consultadas:

- [Spotify Developer Policy](https://developer.spotify.com/policy) e [Web Playback SDK](https://developer.spotify.com/documentation/web-playback-sdk/reference).
- [YouTube IFrame Player API](https://developers.google.com/youtube/iframe_api_reference).
- [Resposta na comunidade oficial Deezer sobre acesso à API](https://en.deezercommunity.com/features-feedback-44/api-access-83418?postid=268359). Essa resposta não comprova disponibilidade de um SDK de reprodução para jogos.
- [Deezer: termos de desenvolvedores](https://cdn-content.dzcdn.net/pdf/CGU-developers.pdf). O documento legado não é prova de concessão atual de acesso.
- [Apple Music / MusicKit](https://developer.apple.com/musickit/).

Nenhuma integração nova de streaming foi ativada; as limitações acima impedem prometer fallback universal. A proposta separa capacidade técnica de autorização de uso.
