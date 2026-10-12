# Exportación MP4 para compartir

Fecha: 10/10/2026, auditada el 11/10/2026 (§3–§13). Complementa a `docs/cine-lossless-audit.md` (máster Cine) y
`docs/cine-playback.md` (reproductor). Estados: **PASS** (medido y correcto), **FAIL**, **NO VERIFICADO** (no se pudo
comprobar aquí), **NO APLICA**.

## 1. Contrato

Tres objetivos separados:

1. **Conservar el máster.** El `.mkv` de Cine (H.264 RGB sin pérdidas, sonido PCM) es la referencia y nunca se toca:
   ni al reproducirlo ni al exportarlo. La exportación escribe otro archivo (`<nombre> (para compartir).mp4` desde
   un máster sin editar, `<nombre> (editado).mp4` si hay cambios). Si falla, el máster sigue igual y se puede repetir.
2. **Reproducir dentro de Stackshot** el máster tal cual (`docs/cine-playback.md`).
3. **Compartir**: un MP4 convencional, que abre cualquier reproductor, **con pérdida** (4:2:0 y AAC). Nunca se
   presenta como copia idéntica del máster; el propio archivo lo dice en su descripción.

Por defecto el MP4 conserva el tamaño (recortado a par) y la frecuencia del origen, no amplía, no cambia la duración
ni quita el sonido. Sólo cambia algo que el usuario elige: tamaño 720p/480p…, velocidad, recorte o cortes.

## 2. Recorrido

| Paso | Dónde |
|---|---|
| Botón «Exportar MP4» del reproductor (vídeo entero) | `PlayerWindow.ExportMp4` |
| «Exportar» del editor (recorte, cortes, marcas, fondo, velocidad, tamaño, varios clips) | `Editor` (`spec` de exportación) |
| Línea de órdenes de FFmpeg, función pura de lo elegido | `VideoExport.Arguments`, `ShareCodec`, `H264Level` |
| Ejecución con progreso y cancelación (Esc, cerrar la ventana) | `VideoExport.Run` / proceso del editor, en un *job object* |
| Comprobación antes de ofrecer el archivo | `ShareCheck.Validate` con `PlayerKit.ExportExpect` o `Editor.ExpectFor` |
| Resultado: a la pila (o «(sin validar)» y el motivo) | `ExportDone` / `CheckDone` |

Lectura con la misma lista blanca de demultiplexores que el reproductor (`Recorder.SafeInput`). Dónde se escribe
(`ShotStack.ExportFolder`): si el vídeo ya está en la carpeta de capturas (un máster guardado), el MP4 va a su lado y
aparece en la pila como guardado; si no, va a la carpeta temporal como cualquier captura, y hay que guardarlo, porque
la limpieza borra lo no guardado una hora después de que deje de estar en la pila. Así desapareció el MP4 de referencia
de §3 a las 18:05:45, según el registro de Stackshot (el análisis se hizo con una copia); desde el 11/10 ya no pasa con
las exportaciones de vídeos guardados. El nombre se reserva al empezar (`ShotStack.Reserve`), así que dos exportaciones
del mismo vídeo a la vez no se pisan.

## 3. El MP4 de referencia y su máster (medido)

`Cine 2026-10-11 17.03.26 (para compartir).mp4`, exportado desde el reproductor a las 17:05:20, y su máster
`Cine 2026-10-11 17.03.26.mkv` (la copia guardada y la temporal tienen el mismo SHA-256). Se analizaron copias; los
originales tienen el mismo SHA-256 y fecha antes y después de todo el trabajo.

| Dato | MP4 de referencia | Máster |
|---|---|---|
| Archivo | 14 132 648 bytes | 112 422 235 bytes |
| Contenedor | MP4 (`isom`), `moov` antes de `mdat` | Matroska |
| Vídeo | H.264 High, **nivel 5.0**, 5 referencias, 3 B, GOP 60 | H.264 High 4:4:4 Predictive (`libx264rgb -qp 0`) |
| Codificador (SEI de x264) | x264 core 165: `crf=10`, preset slow (subme 8, ref 5, trellis 2, lookahead 50), `vbv_maxrate=40000 vbv_bufsize=80000` | — |
| Tamaño | **1888 × 968** (1730 × 786 era la grabación del 10/10), píxel cuadrado | 1888 × 968 |
| Fotogramas | 30 fps; 1155, todos de 1/30 s exacto (512/15360) | 30 fps; 1155 |
| Duración | 38,50 s | 38,50 s |
| Tasa | vídeo 2652 kb/s, sonido 274 kb/s (pedidos 320), total 2936 kb/s | 23,4 Mb/s |
| Píxel | `yuv420p`, 8 bits, 4:2:0 | `gbrp`, 8 bits, RGB completo |
| Color | rango limitado, BT.709 / BT.709 / sRGB; **ubicación del croma sin indicar** | rango completo, GBR / BT.709 / sRGB |
| Pistas | 1 vídeo + 1 AAC-LC 48 kHz estéreo, con el título «Mezcla (equipo + micrófono)» | 1 vídeo + 3 PCM float 48 kHz estéreo (mezcla por defecto, equipo y micrófono originales) |
| Descripción | **«Stackshot Cine: vídeo H.264 RGB sin pérdidas y sonido PCM de 32 bits»** (copiada del máster) | la misma |
| Lectura completa | sin errores (FFmpeg); Windows decodifica entero el mismo vídeo rehecho con esos ajustes (§11) | Windows no la decodifica (por eso hay reproductor propio) |

Sonido: 1 848 000 muestras en los dos, alineadas a la muestra (la diferencia queda en −62,2 dB con la señal en
−19,9 dB; con una muestra de desfase sube a −28,9 dB), mismo nivel (RMS −19,88 / −19,89 dB) y sin recortes (pico
−1,00 → −0,94 dBFS).

Lo que **no** reducía: tamaño, fps, relación de aspecto, duración, número de fotogramas ni longitud del sonido. El CRF
se leyó de la cabecera que escribe x264, no se dedujo de la tasa. Los ajustes de antes, aplicados a la misma copia del
máster con el mismo FFmpeg, **reproducen la referencia imagen por imagen** (1155 de 1155 idénticas).

## 4. Defectos encontrados y causas

| # | Gravedad | Defecto | Causa |
|---|---|---|---|
| 1 | Alta | El MP4 decía ser el máster sin pérdidas, y el panel de información del reproductor lo mostraba como «Máster sin pérdidas: Sí» | FFmpeg copia por defecto los metadatos globales del origen, y `PlayerKit.IsMaster` sólo buscaba el texto «Stackshot Cine» |
| 2 | Media | Toda exportación declaraba **nivel 5.0** aunque cupiera en 4.1 (640×360, 1888×968 y 1080p60, medido); 4K60 declaraba 6.1 | Sin `-level`, x264 elige el más bajo que admite lo pedido: 5 referencias × 7198 macrobloques no caben en los 32 768 de 4.1, y un búfer de 80 000 kbit supera los 78 125 de High 4.1/4.2. Un aparato limitado a 4.1/4.2 (teles y adaptadores de hace unos años) puede rechazarlo o recodificarlo |
| 3 | Media | El tope de 40 Mb/s recortaba CRF 10 en desplazamientos rápidos | Medido con fotogramas de este máster desplazándose 24 px por fotograma: sin tope pide 50,6 Mb/s; con 40 Mb/s pierde 1,4 dB de media y 3,2 dB en el peor fotograma (§11) |
| 4 | Media | Elegir otra pista de sonido en el reproductor o el editor no cambiaba el MP4 | La exportación usaba siempre la primera pista (`0:a:0`) |
| 5 | Media | Un vídeo de frecuencia variable (móvil) perdía fotogramas | El modo por defecto de FFmpeg para MP4 descarta los que caen fuera de su cuadrícula: 2 de 90 en la prueba |
| 6 | Media | La comprobación rechazaba esos vídeos aunque estuvieran bien | La lectura completa redondeaba los tiempos a la frecuencia nominal y daba un error falso (*non monotonically increasing dts*); el editor exigía la frecuencia media ±0,01 a un vídeo que no la tiene constante |
| 7 | Baja | El croma 4:2:0 está centrado (así lo genera swscale) pero el archivo no lo decía, y sin indicación H.264 entiende «a la izquierda» | Los reproductores que siguen la norma desplazaban medio píxel cada borde de color: 1,04 dB menos en este máster (§11) |
| 8 | Baja | La exportación del reproductor no comprobaba la frecuencia ni el número de fotogramas de un máster | Por prudencia con los vídeos de móvil se omitían para todos |
| 9 | Baja | El título de la pista del máster («Mezcla…», «… (original)») pasaba al MP4, y FFmpeg 9 avisa «UDTA parsing failed» al leerlo | Copia por defecto de los metadatos de la pista |

## 5. Cambios

- `VideoExport.H264Level(tamaño, fps)`: el nivel más bajo de 4.1, 4.2, 5.0, 5.1, 5.2, 6.0, 6.1 y 6.2 que admite el
  fotograma (macrobloques, ancho y alto máximos) a esa frecuencia; por encima de 6.2, x264 elige. Nunca por debajo de 4.1.
- `ShareCodec(calidad, tamaño, fps)`: pasa `-level:v` (x264 ajusta entonces las referencias al búfer de imágenes del
  nivel) y el tope de tasa de ese nivel en High, como mucho 160 Mb/s, con el búfer como mucho el doble del tope.
- `-fps_mode passthrough`: cada fotograma sale con su tiempo; la frecuencia sólo cambia donde lo dice un filtro
  (velocidad, cortes, varios clips). Con un máster el resultado es idéntico al de antes (paquetes y tiempos iguales).
- Fuentes RGB: `scale=out_chroma_loc=center` antes de pasar a 4:2:0. Los píxeles son los mismos (las 60 imágenes de
  la prueba son idénticas con y sin él); sólo se escribe en el archivo dónde va el croma. Las fuentes YUV no cambian.
- Metadatos: `comment="Stackshot: MP4 para compartir (H.264 y AAC, con pérdida)"` y sin título de pista.
  `PlayerKit.IsMaster` exige además píxel RGB (`gbr…`), así que tampoco los MP4 exportados antes pasan por máster.
- Pista de sonido: `ExportSpec.AudioTrack` con la que se está escuchando (reproductor: `Playback.Track`; editor: el
  menú de sonido; varios clips: `VideoExport.ClipTrack`, la elegida si el clip la tiene y si no la suya por defecto).
- Comprobación (`ShareCheck`): nueva comprobación «Nivel H.264» leída de la cabecera del propio archivo
  (`trace_headers`), «Color» exige también el croma centrado cuando se generó aquí, y la lectura completa conserva
  los tiempos del archivo. El reproductor comprueba frecuencia y fotogramas de un máster (`PlayerKit.ExportExpect`).
  El editor sólo comprueba la frecuencia si es constante (`Editor.ConstantRate`: fps igual a tbr).
- Sin cambios: CRF por calidad (10/14/20), preset (slow hasta 2,4 Mpx, medium por encima), GOP de 2 s, sonido, GIF y
  las grabaciones Estándar/Alta/Máxima (`Recorder.FinalCodec`; 5980 escenarios idénticos a la referencia).

## 6. Parámetros finales

| Calidad (opción «Calidad» del editor, `ExportQuality`) | Vídeo | Sonido |
|---|---|---|
| **Máxima** (por defecto) | CRF 10 | AAC-LC 320 kb/s |
| Alta | CRF 14 | AAC-LC 256 kb/s |
| Ligera | CRF 20 | AAC-LC 160 kb/s |

Común: `libx264`, H.264 High, `yuv420p`, `-preset slow` hasta 2,4 Mpx (`medium` por encima), GOP de 2 s, 48 kHz
estéreo, `-movflags +faststart`, `-fps_mode passthrough`. Nivel y tope:

| Nivel | Típico | Tope / búfer |
|---|---|---|
| 4.1 | hasta 1080p30 (1888×968, 1730×786…) | 62,5 / 78,125 Mb |
| 4.2 | 1080p60 | 62,5 / 78,125 Mb |
| 5.0 | 1920×1200, 1440p30 | 160 / 168,75 Mb |
| 5.1 | 4K30, 1600p60 | 160 / 300 Mb |
| 5.2 | 4K60 | 160 / 300 Mb |
| 6.0 / 6.1 / 6.2 | 8K, 4K a más de 60 fps (pocos aparatos los reproducen) | 160 / 300 Mb (320 en 6.1 y 6.2) |
| (x264 decide) | por encima de 6.2 | 160 / 300 Mb |

Máster 1888×968 a 30 fps, tal como sale ahora:

```
-filter_complex "[0:v]scale=out_color_matrix=bt709:out_range=tv:flags=accurate_rnd+full_chroma_int+full_chroma_inp,
  format=yuv444p,crop=1888:968:0:0,setparams=color_primaries=bt709:color_trc=iec61966-2-1:colorspace=bt709:range=tv,
  scale=out_chroma_loc=center,format=yuv420p[o]" -map "[o]" -fps_mode passthrough
-c:v libx264 -preset slow -crf 10 -profile:v high -level:v 4.1 -g 60 -maxrate 62500k -bufsize 78125k
-map 0:a:0 -c:a aac -b:a 320k -ar 48000 -ac 2
-metadata comment="Stackshot: MP4 para compartir (H.264 y AAC, con pérdida)" -metadata:s:a:0 title= -movflags +faststart
```

**Por qué CRF 10 sigue siendo «Máxima»** (§11): queda a 0,11 dB del techo de cualquier 4:2:0; bajar a CRF 14/16/18
reduce el archivo un 19/26/33 % y pierde 0,14/0,25/0,41 dB, y lo que más pierde es el texto de la terminal
(0,20/0,35/0,58 dB). `preset slower` no mejora nada (+0,00 dB) y tarda un 21 % más. CRF no es una medida absoluta:
estos números valen para x264 y este contenido.

**Tamaño objetivo: 16 MB (WhatsApp) y 25 MB (correo)** (11/10/2026). En vez de una calidad fija, el archivo se ajusta a
un tamaño: 15,5 MB y 24 MB de objetivo (margen para el contenedor y para servicios que cuentan en MiB). Sonido AAC a
128 kb/s; el vídeo se lleva el resto con un 2 % de reserva: `kb/s = (bytes × 8 × 0,98 / segundos − 128 000) / 1000`
(`VideoExport.TargetKbps`). Se codifica en **dos pasadas** (`-pass 1 … -an -f null NUL`, luego `-pass 2` con
`-b:v N k`, `-maxrate` hasta 2N sin pasar del tope del nivel y `-bufsize` acorde), con el registro de pasadas en una
carpeta temporal que se borra al acabar (también si se cancela). La comprobación añade «Límite de tamaño». Si el vídeo
es tan largo que el vídeo se quedaría por debajo de 150 kb/s no se exporta y se avisa (en el reproductor: marcar un
tramo más corto; en el editor: recortar, acelerar o bajar a 720p).

Medido: 8 s de ruido 1280×720 a 30 fps (lo más difícil de comprimir; con CRF 10 pasaría de 40 MB) en 16 MB: 14,22 MiB
(14,9 MB, por debajo del objetivo de 15,5 MB), comprobación correcta, 23 s en total. Un vídeo de 8 minutos en 16 MB se
rechaza; un tramo de 60 s del mismo vídeo cabe. El aviso dice cuánto cabe (`VideoExport.MaxLength`: a 16 MB unos 7 min
con sonido, a 25 MB unos 11).

**Reintento si no cabe** (11/10/2026, tercera revisión). El control de tasa de dos pasadas de x264 no es exacto: en 30 s
de 640×360 con mucho movimiento la pista de vídeo salió a 4,26 Mb/s para un objetivo de 3,92 (+8,6 %, 16,48 MB, por
encima del límite y la comprobación lo rechazaba); en 4K60 de 2 s, +0,7 a +1,3 %. Sin `-maxrate`/`-bufsize` pasa lo
mismo, así que no es el búfer. Ahora, si tras la segunda pasada el archivo supera el límite, esa pasada se repite con la
tasa multiplicada por límite/tamaño × 0,97 (`VideoExport.Refit`, `ExportSpec.Fit`), dos veces como mucho, en el editor y
en el reproductor; el registro de la primera pasada se reutiliza. Un clip muy corto que podría permitirse más que el
techo del nivel recibe el techo y sale más pequeño. El editor también exporta un MP4 sin editar cuando la calidad es un
tamaño (antes se cerraba sin hacer nada) y una duración desconocida se dice como tal.

**Cortes en MPEG-TS con GOP largo.** Los TS y MPEG-PS no tienen índice: se busca antes del corte y se descarta lo
decodificado de más (`PlayerArgs.Early`). Con 3 s fijos, un TS de x264 con su GOP por defecto (250 fotogramas, más de 8 s
a 30 fps) perdía los fotogramas hasta el siguiente fotograma clave: 60 de 120 en dos tramos de 2 s. Ahora, al sondear el
archivo, se mide el hueco mayor entre fotogramas clave de sus dos primeros minutos decodificando solo los clave
(`PlayerArgs.LearnKeyGap`, rápido) y la búsqueda retrocede ese hueco más medio segundo (hasta 60 s). Límite conocido: un
archivo cuyo GOP cambie después de esos dos minutos (raro: los codificadores usan uno para todo el archivo) no se mide
ahí; medido en `h4editor/ts-gop-after-four-minutes-not-learned`.

| Calidad | Vídeo | Sonido |
|---|---|---|
| 16 MB (WhatsApp) | 2 pasadas, 15,5 MB de objetivo | AAC-LC 128 kb/s |
| 25 MB (correo) | 2 pasadas, 24 MB de objetivo | AAC-LC 128 kb/s |

## 7. Resolución, frecuencia y tiempos

- Tamaño del origen, recortado a par (4:2:0 lo exige: un origen impar pierde la última columna o fila); nunca se amplía.
  Sólo cambia si se elige otro tamaño (Lanczos, sin deformar).
- Frecuencia del origen, también 60 fps. Un vídeo de frecuencia variable conserva cada fotograma en su tiempo (90 de
  90 en la prueba). Al cambiar la velocidad o unir trozos se fija una frecuencia constante explícita (`fps=`): acelerar
  conserva todos los fotogramas hasta max(60, fps del origen); ralentizar, la del origen.
- El MP4 de un máster de 30 fps tiene 1155 fotogramas de 1/30 s exacto (como el anterior) y la misma duración.

## 8. Color

- RGB → YUV con la matriz **BT.709**, rango limitado y redondeo exacto (`VideoExport.RgbToYuv`), etiquetado
  BT.709 (primarios y matriz) con transferencia sRGB: los valores RGB de la pantalla ya vienen en sRGB y no se
  transforman, así que la etiqueta dice la verdad. Parches de color dentro de 1 nivel; rampa de grises con 0,18 niveles
  de error medio (control con BT.601: hasta 22 niveles).
- Croma 4:2:0 **centrado y dicho en el archivo** cuando se genera aquí; las fuentes YUV conservan sus etiquetas y su
  croma. Al unir clips, un clip BT.601 o de rango completo se convierte a BT.709 antes.
- La pérdida que queda es la de 4:2:0: medio croma en cada dirección. Se nota en líneas finas de color saturado (el
  aro rojo de un avatar sale más apagado), apenas en el texto.

## 9. Sonido

- Una pista AAC-LC 48 kHz estéreo: **la que se está escuchando** (en un máster, la mezcla por defecto). No se mezclan
  pistas por su cuenta. Mono sale como estéreo. Sin sonido en el origen, sin pista.
- Duración, inicio y final exactos (1 848 000 muestras de 1 848 000 en el máster de referencia) y sincronía 0 ms.
- AAC tiene pérdida: el máster conserva el PCM original.

## 10. Comprobación automática (`src/Editor/ShareCheck.cs`)

Tras exportar, en segundo plano («Comprobando el vídeo…», cancelable). Si algo falla el archivo se queda como
«(sin validar)», no va a la pila y se dice qué falló; el máster no se toca.

1. Existe y pasa de 1 KB. 2. Estructura MP4: `ftyp` primero y las cajas suman el archivo (no está cortado).
3. Inicio rápido: `moov` antes de `mdat`. 4. Un vídeo H.264 High/Main/Constrained Baseline en `yuv420p`.
5. Tamaño exacto. 6. fps ±0,01 (si la frecuencia es constante). 7. **Nivel H.264** el esperado (leído de la cabecera).
8. Color: etiquetas BT.709 y, si el croma se generó aquí, croma centrado. 9. Sonido: una pista AAC-LC 48 kHz
estéreo, o ninguna. 10. Duración ±max(0,15 s, 2 fotogramas). 11. Lectura completa sin errores, con los tiempos del
archivo. 12. Número de fotogramas ±1 (más 2 por unión).

## 11. Benchmark

`tests\editor\run-tests.ps1 sharebench` con `STACKSHOT_BENCH_MASTER` = copia del máster de referencia y
`STACKSHOT_BENCH_REFERENCE` = copia del MP4 de referencia (variables en `tests/editor/EditorTestsShareBench.cs`).
Cada variante pasa por `VideoExport.Arguments`; sólo cambia la parte del codificador. PSNR y SSIM en RGB: el `gbrp`
del máster contra el MP4 decodificado con BT.709 de rango limitado, fotograma a fotograma. «PSNR norma»: el croma donde
dice el archivo (sin indicación, a la izquierda, como manda H.264). Zonas: terminal (texto de colores, 0–7,4 s),
marcadores (barra del navegador, texto pequeño con iconos, 8–38,4 s) y títulos (texto sobre miniaturas, 16–38,4 s).
Portátil de este equipo, 38,5 s de vídeo.

| Variante | Vídeo | MB | Tiempo | CPU | Nivel | PSNR | Peor | PSNR norma | SSIM | Terminal | Marcadores | Títulos | Windows |
|---|---|---|---|---|---|---|---|---|---|---|---|---|---|
| Referencia (tu MP4) | 2,65 Mb/s | 13,5 | — | — | 5.0 | 39,43 | 37,65 | 38,39 | 0,99352 | 39,88 | 35,19 | 39,40 | — |
| Antes (reproduce la referencia) | 2,65 Mb/s | 13,5 | 28,3 s | 129 s | 5.0 | 39,43 | 37,65 | 38,39 | 0,99352 | 39,88 | 35,19 | 39,40 | sí |
| **Máxima (CRF 10)** | 2,65 Mb/s | 13,5 | 24,6 s | 127 s | 4.1 | 39,43 | 37,65 | **39,43** | 0,99352 | 39,88 | 35,19 | 39,40 | sí |
| CRF 12 | 2,37 Mb/s | 12,2 | 24,7 s | 126 s | 4.1 | 39,37 | 37,59 | 39,37 | 0,99329 | 39,80 | 35,20 | 39,36 | sí |
| Alta (CRF 14) | 2,11 Mb/s | 11,0 | 25,7 s | 130 s | 4.1 | 39,29 | 37,50 | 39,29 | 0,99300 | 39,68 | 35,13 | 39,32 | sí |
| CRF 16 | 1,88 Mb/s | 9,9 | 22,0 s | 125 s | 4.1 | 39,18 | 37,36 | 39,18 | 0,99258 | 39,53 | 35,07 | 39,24 | sí |
| CRF 18 | 1,68 Mb/s | 9,0 | 25,5 s | 127 s | 4.1 | 39,02 | 37,16 | 39,02 | 0,99206 | 39,30 | 35,04 | 39,12 | sí |
| Ligera (CRF 20) | 1,49 Mb/s | 8,2 | 22,6 s | 124 s | 4.1 | 38,78 | 36,88 | 38,78 | 0,99133 | 38,95 | 34,89 | 38,96 | sí |
| CRF 10, preset slower | 2,66 Mb/s | 13,5 | 29,8 s | 133 s | 4.1 | 39,43 | 37,65 | 39,43 | 0,99353 | 39,88 | 35,18 | 39,40 | sí |
| CRF 10, 4:4:4 (High 4:4:4) | 2,88 Mb/s | 14,5 | 29,3 s | 139 s | 4.1 | 50,97 | 48,20 | 50,97 | 0,99874 | 49,93 | 49,72 | 55,51 | **no** |
| Techo 4:2:0 (sin codificar) | — | — | — | — | — | 39,54 | 37,78 | — | 0,99392 | 40,02 | 35,26 | 39,46 | — |

Sonido 274 kb/s en todas. Memoria máxima de FFmpeg: unos 900 MB (1,4 GB con 4:4:4). Todas las variantes 4:2:0 pasan
la comprobación (12 puntos; la de antes, 11) y Windows (Media Foundation) las decodifica enteras.

Desplazamiento rápido (6 s sintéticos hechos con cuatro fotogramas del máster, 24 px por fotograma, CRF 10):

| Tope | Vídeo | PSNR medio | Peor fotograma | Nivel |
|---|---|---|---|---|
| Antes (40 / 80 Mb) | 39,3 Mb/s | 38,60 dB | 34,51 dB | 5.0 |
| **Ahora (62,5 / 78,125 Mb)** | 46,2 Mb/s | 39,75 dB | 36,98 dB | 4.1 |
| Sin tope | 50,6 Mb/s | 40,03 dB | 37,75 dB | 5.0 |

Con fotogramas propios de 1920×1080 a 30 fps (otra prueba de esfuerzo, más densa): sin tope 78 Mb/s y 38,29 / 37,25 dB;
antes 34,89 / 31,55 dB; ahora 37,39 / 35,42 dB. En 1440p30 y 4K30 (preset medium) el tope nuevo nunca empeoró y subió
el peor fotograma unos 2 dB.

Lectura:

- **Máxima ya estaba en el techo**: a 0,11 dB de lo máximo que permite 4:2:0. Lo que se pierde respecto al máster es
  el submuestreo del color, no el codificador.
- El cambio de nivel no cuesta calidad (4 referencias en vez de 5: los mismos 39,43 dB y 8 KiB más).
- En los reproductores que siguen la norma H.264, el archivo nuevo se ve **1,04 dB mejor** que el anterior (croma en
  su sitio); en los que ignoran la indicación se ve igual que antes.
- 4:4:4 recupera 11,5 dB (los bordes de color quedan como en el máster) por un 8 % más de tamaño, pero Windows no lo
  decodifica (medido: Media Foundation falla al leerlo), y según sus especificaciones públicas tampoco Safari, el
  iPhone ni la mayoría de decodificadores por hardware de los móviles (NO VERIFICADO aquí): no sirve como formato para
  compartir. No se ofrece (§15).

Recortes a 3× (el máster y cada variante, en la carpeta de salida del benchmark): el texto es indistinguible entre el
máster, Máxima y CRF 18; las líneas finas de color saturado salen más apagadas en todas las 4:2:0 y como el máster en
4:4:4.

## 12. Comparación del MP4 nuevo con el anterior

| | Anterior | Nuevo |
|---|---|---|
| Tamaño, fps, duración, fotogramas | 1888×968, 30, 38,50 s, 1155 | iguales |
| Imagen (PSNR / SSIM contra el máster) | 39,43 dB / 0,99352 | 39,43 dB / 0,99352 |
| Con un reproductor que sigue la norma | 38,39 dB | 39,43 dB |
| Nivel H.264 / referencias | 5.0 / 5 | 4.1 / 4 |
| Tope en movimiento rápido | 40 Mb/s | 62,5 Mb/s (máximo de 4.1) |
| Archivo | 13,5 MB | 13,5 MB (+8 KiB) |
| Descripción | «… sin pérdidas y sonido PCM» | «MP4 para compartir (H.264 y AAC, con pérdida)» |
| Panel de información del reproductor | «Máster sin pérdidas: Sí» | sin esa fila |
| Comprobación | 9 puntos (reproductor) | 12 (frecuencia, fotogramas, nivel y croma incluidos) |

## 13. Pruebas ejecutadas

Todas con FFmpeg 9.0.2 en este equipo, carpetas temporales propias y copias del máster.

| Suite | Resultado |
|---|---|
| `tests\editor`, todas las suites por defecto (args export strip atomic keep tools player playerwindow share windows clips huntexport huntplayer hunttools) | 285 PASS, 1 FAIL, 2 SKIP. El fallo era una prueba que aún esperaba el tope antiguo de 1080p60 (60M/120M); corregida, `clips` repetida: 36 PASS. Omitidas: un MP4 del 10/10 que ya no existe en este equipo y una ruta de 320 caracteres que .NET 4.8 no admite |
| `tests\editor` share (37, de ellas 8 de la auditoría en `EditorTestsShareAudit.cs`) | 37 PASS |
| `tests\cine` regression y args (perfiles de grabación) | 13 PASS: 5980 escenarios idénticos a la referencia (salvo 6 de `FinalCodec q=3`, ya conocidos y comprobados) |
| `tests\editor` sharebench sobre el máster de referencia | PASS; máster con el mismo SHA-256 antes y después |
| `build.ps1` (sin instalar) | compila con `/warnaserror+` |

Pruebas nuevas de la auditoría: nivel declarado en el propio archivo para 640×360, 1888×968, 1080p60 y 1920×1200 (y
5.0 con los ajustes de antes); la descripción del máster no pasa al MP4 y ni el MP4 nuevo ni uno exportado antes se
toman por máster; la pista elegida (equipo o micrófono) llega al MP4 entera, con cortes y con velocidad; un vídeo de
frecuencia variable conserva 90 de 90 fotogramas en su tiempo y pasa las comprobaciones del reproductor y del editor;
el croma centrado no cambia ningún píxel y la comprobación nota su ausencia; la comprobación rechaza un nivel 5.0; la
exportación desde la ventana del reproductor usa la pista que suena y comprueba frecuencia, fotogramas y nivel; dos
exportaciones seguidas (la segunda como «(2)»), un máster cortado después de abrirlo (queda como «(sin validar)»:
0,7 s de 2 s, 21 fotogramas de 60) y cerrar la ventana durante la exportación (borra el archivo a medias). Ya había
pruebas de cancelación con Esc, nombre de salida ocupado, salida de solo lectura, origen con duración falsa, sin
sonido, 60 fps, mono y archivos que la comprobación debe rechazar.

## 14. Varios vídeos en una exportación

«+ Vídeo» en la línea de tiempo (o soltar un archivo en el editor) añade clips de cualquier formato que lea el
reproductor (MP4, MOV, MKV y másters Cine, WebM, AVI, WMV, FLV, MPG, TS, OGV, GIF…). El primero fija el lienzo
(tamaño y fps); los demás se escalan con Lanczos sin deformar, con bandas negras centradas, píxel cuadrado y la
frecuencia del lienzo; el sonido a 48 kHz estéreo (la pista elegida si el clip la tiene), y un clip sin sonido aporta
silencio de su duración exacta. Recorte, cortes, velocidad, marcas y tamaño se aplican sobre toda la secuencia.

Resultados de la primera versión que siguen valiendo: Windows decodifica el MP4 de varios clips (105 fotogramas,
sonido 3,54 s); dos clips de distinto tamaño, fps y sonido; un corte que cruza una unión; MP4 + TS + WMV + FLV + MPG en
una exportación; recortes exactos de TS/MPG/M2TS; un clip BT.601 conserva su color; GIF exportado con todos los
colores (paleta `stats_mode=full`); tamaños estrictamente decrecientes Máxima > Alta > Ligera.

## 15. Limitaciones

- **NO VERIFICADO**: reproducción en móviles, teles y navegadores reales. Sólo Windows (Media Foundation) y FFmpeg.
  El nivel 4.1/4.2 es lo que esos aparatos anuncian que admiten, pero no se ha probado en ninguno.
- **NO VERIFICADO**: disco lleno de verdad (no se puede crear un volumen pequeño sin permisos de administrador). Pasa
  por el mismo camino que un error de FFmpeg, que sí está probado: se borra el archivo a medias y se avisa.
- Los reproductores que ignoran la ubicación del croma y suponen «izquierda» siguen viendo el desplazamiento de medio
  píxel, como antes del cambio.
- La pérdida de 4:2:0 en líneas finas de color es inevitable en un MP4 compatible. 4:4:4 la evita (+11,5 dB medidos)
  pero no lo decodifican Windows ni la mayoría de móviles; podría añadirse como opción aparte, avisando, si se pide.
- Con 1888×968 y preset slow FFmpeg usa unos 900 MB de memoria y unos 5 núcleos durante la exportación.
- En el movimiento más rápido el tope de 4.1 todavía recorta algo (0,3 dB de media frente a sin tope); pasar de él
  exigiría nivel 5, que es justo lo que se quería evitar.
- Velocidad, cortes o varios clips convierten un vídeo de frecuencia variable en frecuencia constante (a propósito).
- AAC pide 320 kb/s y gasta lo que necesita (274 kb/s aquí); con pérdida, como todo AAC.
- La grabación directa a GIF (no la exportación) sigue usando `stats_mode=diff`.
