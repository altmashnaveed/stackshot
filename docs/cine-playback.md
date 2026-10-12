# Reproducción integrada de Stackshot

Fecha: 10/10/2026. Complementa a `docs/cine-lossless-audit.md` (máster Cine) y `docs/cine-share-export.md` (MP4 para
compartir). Estados: **PASS** (medido y correcto), **FAIL**, **NO VERIFICADO** (no se pudo comprobar aquí), **NO
APLICA**.

## 1. Por qué hace falta un reproductor propio

El máster Cine es H.264 RGB sin pérdidas (`libx264rgb`, `gbrp`) con sonido PCM float en `.mkv`. El reproductor de
Windows (Media Foundation) **no lo decodifica**: medido con un Source Reader de Media Foundation llamado desde las
pruebas (`tests/editor/EditorTestsWindows.cs`, `MfDecode`), el máster falla y el MP4 exportado se lee entero. Antes la
única salida era VLC, mpv o un editor. Ahora Stackshot lo reproduce por sí mismo, **sin convertir el máster y sin
software externo** aparte del FFmpeg que ya descarga para grabar.

## 2. Motor (`src/Editor/Player.cs`)

- **Imagen**: un FFmpeg decodifica a `rawvideo bgra` por una tubería, a frecuencia constante (`-fps_mode cfr -r <fps>`),
  ya escalado al tamaño de la vista (Lanczos o bilineal, **nunca amplía**). Los fotogramas van a un grupo de búferes
  reutilizados; si la pantalla no llega, se descartan y se cuentan.
- **Sonido**: un segundo FFmpeg da la pista elegida en `f32le` 48 kHz estéreo a `waveOut`. El reloj de reproducción es
  el del sonido (muestras reproducidas); sin sonido, un `Stopwatch`.
- **Velocidad** 0,25×–4×: imagen con `setpts=PTS/s` antes de la frecuencia constante; sonido con una cadena de
  `atempo` (cada etapa entre 0,5 y 2), conservando el tono.
- **Saltos**: reinician ambos procesos en el punto exacto. Sin `-ss` en el instante 0 (el demultiplexor FLV no sabe
  «saltar» a 0 y no da nada). Los contenedores sin índice (**MPEG-TS/PS**: `.ts .mts .m2ts .mpg .vob`…) saltan 3 s
  antes y descartan después lo decodificado antes del punto: un salto normal en ellos empezaba hasta un GOP tarde.
- **Pausa**: muestra el fotograma exacto (`Still`, la última petición gana). Avance y retroceso fotograma a fotograma,
  repetición al final, pistas de sonido con su título, volumen.
- **Formatos**: la misma lista blanca de demultiplexores para reproductor, editor y exportación
  (`Recorder.SafeInput`): mov/mp4/m4a/3gp/3g2/mj2, matroska/webm, gif, avi, asf (WMV), flv, mpeg, mpegts, ogg, mxf,
  rm, wtv, dv, nut, ivf, gxf. Fuera quedan listas y todo lo que nombra otros archivos o direcciones (hls, concat, dash,
  tee, lavfi, image2, http…). Extensiones conocidas en `ShotStack.VideoExtensions` (38); una desconocida se sondea por
  contenido antes de decidir.
- **Sin duración en la cabecera** (WebM grabado en el navegador, MKV interrumpido): la duración se obtiene leyendo
  los paquetes de vídeo sin decodificar (`MediaInfo.ScanDuration`, una vez por archivo).
- Cada FFmpeg va en un *job object* (`ChildJob`) y se mata al cerrar; las pruebas cuentan procesos antes y después.

## 3. Ventana de reproducción (`src/Editor/PlayerWindow.cs`)

Oscura, mínima, con los iconos y menús de Stackshot. Imagen centrada con bandas negras; barra inferior que se oculta a
los 2,5 s sin mover el ratón mientras reproduce. Reproducir/pausa, tiempo actual/total, barra de posición (clic y
arrastre, tiempo al pasar), volumen y silencio, menú de pistas (2 o más), velocidad, repetir, pantalla completa,
**Editar** (abre el editor), **Exportar MP4** (para fuentes que no son MP4; pasa por la misma comprobación que el
editor), Mostrar en carpeta, panel de información (I: contenedor, códec, píxel, tamaño, fps, duración, pistas, tamaño
de archivo y, para un máster Cine, «Máster sin pérdidas» y el resultado de su verificación). Teclas: Espacio/K,
←/→ 5 s, J/L 10 s, `,`/`.` fotograma, ↑/↓ volumen, M, F/F11, I, Inicio/Fin, Ctrl+O. Recuerda volumen, escalado y
repetición. Admite soltar un archivo encima.

Apertura: las miniaturas, los recientes y la bandeja abren los vídeos en esta ventana cuando
`Grabación > Después de grabar > Abrir los vídeos en Stackshot` está activo (por defecto); un máster Cine siempre.
Bandeja: «Abrir vídeo…»; ventana principal: «Abrir un vídeo o un máster Cine…» en Grabación. En el editor, el botón de
reproducir usa el mismo motor y salta los trozos cortados.

**Desde Windows** (11/10/2026): `General > Archivos > Abrir .mkv con Stackshot` registra Stackshot para `.mkv` como
ya se hacía con `.md` y `.xml` (`FileAssoc.Video`, sólo claves propias en HKCU: su ProgID, «Abrir con» y la entrada de
Aplicaciones predeterminadas). Windows deja la elección final al usuario, así que se abre su página para elegir
Stackshot; nunca se cambia la aplicación predeterminada por su cuenta ni la de otra aplicación. Un vídeo que llega por
la línea de órdenes (doble clic, «Abrir con», arrastrar sobre el `.exe`) se abre en el **reproductor** de la copia de
Stackshot que ya está en marcha (`Installer.OpensHere`; antes iba al editor suelto); una imagen o un GIF siguen yendo
al editor y `--edit` sigue forzándolo. Desinstalar quita también esta asociación.

**Tamaño** (11/10/2026): la ventana se abre con el vídeo **a tamaño real** (un píxel del vídeo, un píxel de la
pantalla) siempre que quepa en la pantalla con su marco; si no cabe, a lo más grande que quepa. Antes nunca pasaba del
80 % de la pantalla: un máster de 1888×968 en un portátil 1080p (1920×1032 útiles) se veía a 1536×787, el 81 %. La
imagen **nunca se amplía** por defecto: a pantalla completa o maximizada queda a tamaño real y centrada (antes se
estiraba con interpolación y el texto perdía nitidez); el menú tiene «Imagen: tamaño real (la más nítida)» e «Imagen:
llenar la ventana» (`PlayerFill`, recordado). El panel de información dice a qué escala se ve («En pantalla: 100 %
(tamaño real…)», o el porcentaje y qué hacer si es menor). Un vídeo estrecho (vertical de móvil) a tamaño real abre una
ventana sólo tan ancha como pide la barra (560 px), sin bandas negras arriba y abajo. Los vídeos de **píxeles no
cuadrados** (cámaras HDV: 1440×1080 con SAR 4:3) se ven con su forma, a 1920×1080, y el panel lo dice
(`MediaInfo.Display`, leído del «[SAR … DAR …]» de FFmpeg).

**Seguir donde lo dejaste, tramos, calidad y fotogramas** (11/10/2026):

- Un vídeo de más de 2 minutos se abre donde se dejó la última vez («Sigue donde lo dejaste (12:34) · Inicio vuelve al
  principio»). Se guarda al cerrar la ventana o al abrir otro vídeo en `%LOCALAPPDATA%\Stackshot\posiciones.txt`
  (`PlayerKit.ResumeAt`/`ResumeSave`): una línea por vídeo con su tamaño y fecha de escritura, así que un archivo
  cambiado no hereda la posición; la ruta se compara sin distinguir mayúsculas. Si apenas se vio (menos de 10 s, o se
  abrió y cerró enseguida) se conserva la posición que tenía, y se olvida si se vio hasta el final (últimos 15 s). Se
  guardan los 200 últimos. Con `Settings.ReadOnly` (pruebas, modo portátil de sólo lectura) no se escribe el archivo
  real.
- `E` marca el inicio del tramo y `S` el final (en el fotograma que se ve; el final incluye ese fotograma); el tramo se
  dibuja en amarillo sobre la barra y `Esc` lo quita. Con tramo, el botón pasa a «Exportar tramo» y crea
  «<nombre> (tramo).mp4» sólo con esa parte, con la misma comprobación que el resto. Con tramo también se exporta un
  `.mp4` (sin tramo un `.mp4` a Máxima sería copiarlo, así que no se ofrece).
- Más opciones > «Calidad al exportar» elige Máxima, Alta, Ligera, 16 MB (WhatsApp) o 25 MB (correo); es la misma
  opción que la «Calidad» del editor (`ExportQuality`) y la ayuda del botón la dice. Con 16 o 25 MB también se puede
  exportar un `.mp4` entero (para que quepa). Ver `docs/cine-share-export.md` §6.
- `C` (o el menú «Capturar este fotograma») guarda el fotograma que se ve a su tamaño real, con los píxeles no
  cuadrados ya cuadrados (un HDV 1440×1080 sale a 1920×1080), decodificado igual que lo muestra el reproductor. Con la
  pila (lo normal) entra como una captura más («Fotograma <nombre>»: copiar, editar, guardar, fijar, copiar texto); el
  reproductor suelto lo guarda junto al vídeo como «<nombre> fotograma 0.12.png». Medido: idéntico píxel a píxel al
  fotograma que da FFmpeg (diferencia media 0,00 en un vídeo 4:4:4 sin pérdidas).

## 4. Medidas (este equipo)

| Prueba | Resultado |
|---|---|
| 1080p60, 10 s, a tamaño nativo | 0 descartes, PASS |
| Decodificación 1080p nativo / a 960×540 | 89–108 / 254 fps |
| Decodificación 4K nativo / a 1080p | 15–16 / 73–78 fps (el 4K60 nativo no llega; a tamaño de ventana sí) |
| 4K60 en una ventana de 480×270 | 115 mostrados, 2 descartados en 4,25 s, sin atascos, PASS |
| Sincronía imagen/sonido | 0 ms de desfase medido, PASS |
| 2× con sonido | 90 fotogramas de medio en ~1,5 s, tono conservado, PASS |
| 0,25× | sonido 4× más largo, tono conservado (≈80 ms más corto por la cola de `atempo`), PASS |
| 14 contenedores (mov mkv webm avi wmv flv mpg ts mts m2ts 3gp m4v ogv gif) | se reproducen hasta el final con los fotogramas correctos, PASS |
| Saltos en ts/mpg/m2ts con un fotograma clave por segundo | a ≤1 fotograma del punto pedido en 1,3/2,2/3,5/4,1 s, PASS |
| Archivo ilegible, vacío, solo sonido o inexistente | mensaje claro, sin bloqueo ni excepción, PASS |
| 20 ventanas abiertas y cerradas reproduciendo | 53 FFmpeg lanzados, 0 vivos al final, PASS |
| Máster: SHA-256 y fecha antes/después de reproducir | idénticos, PASS |
| `waveOut` real | suena (prueba con el dispositivo real), PASS |
| Máster 1888×968 en este portátil (1920×1032 útiles, marco 16×39) | se abre a 1888×968, tamaño real (antes 1536×787), PASS |
| Ventana mayor que el vídeo | imagen a tamaño real y centrada; «llenar la ventana» la amplía, PASS |
| Asociación `.mkv` (en una rama de registro de prueba) | ProgID, «Abrir con» y Aplicaciones predeterminadas; la predeterminada y las de otras aplicaciones intactas; se quita sola, PASS |

## 5. Sin VLC

Comprobado: el máster se reproduce, se pausa, salta, cambia de pista y de volumen dentro de Stackshot con sólo
`ffmpeg.exe` (el mismo que descarga para grabar, con SHA-256 fijado) y `winmm.dll` de Windows. Ninguna dependencia
nueva, ningún códec del sistema.

## 6. Limitaciones

- **NO VERIFICADO**: instalación limpia en otro Windows (no hay VM ni Sandbox en este equipo); los clics reales sobre
  la ventana (arrastre de la barra, menús, pantalla completa, soltar desde el Explorador) se han probado por código y
  con capturas fuera de pantalla, no a mano.
- HDR y 10 bits se muestran convertidos a 8 bits (BGRA).
- El 4K60 a tamaño nativo no se decodifica en tiempo real en este portátil por CPU; a tamaño de ventana sí.
- Entre dos clips del editor el cambio de clip puede tardar < 100 ms (se arranca el siguiente proceso).
- Un archivo de un solo fotograma en repetición relanza FFmpeg continuamente mientras la repetición está activa.
