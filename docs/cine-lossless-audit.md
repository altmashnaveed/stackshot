# Cine sin pérdidas — auditoría, diseño y verificación

Estado: implementado y verificado el 09/10/2026 (resultados en §9 y §10).
Equipo de pruebas (el único en el que se ha comprobado nada de lo que sigue): Lenovo ThinkPad, Intel Core i7-1365U
(2P+8E, 12 hilos), 32 GB, Windows 11 Pro 26200, una pantalla 1920×1080 que DXGI
declara a **143,999 Hz**, escala 125 %, SDR. FFmpeg 9.0.2 (compilación «essentials» de gyan.dev, la que descarga
Stackshot). No hay ffprobe: todo se sondea con `ffmpeg`.

## 1. Arquitectura inicial (v2.0.0, commit a7bf44f)

```
DXGI Desktop Duplication (BGRA 8 bits, hilo de recogida)        WASAPI (loopback + micrófono, float 48 kHz estéreo)
  └─ historial de 6 imágenes con su hora de presentación          └─ cada paquete colocado en la línea de tiempo por QPC
Hilo de fotogramas: corte por fotograma en la fase tranquila        (huecos → silencio, solapes → se descartan,
  del refresco, imagen = la última presentada antes del corte       deriva → ±1 muestra con interpolación lineal)
  └─ cursor dibujado en CPU (BGRA) o en GPU (NV12)                 └─ ~grabando … equipo.f32 / micro.f32
Anillo GPU de 3 → staging → FramePool (≤32 búferes, ≤256 MB) → cola → hilo escritor → tubería stdin de FFmpeg
FFmpeg en vivo
  Estándar/Alta: BGRA → scale (BT.709, rango limitado) → yuv420p → libx264 CRF 23/16 (o HW si el área es grande)
  Máxima/Cine:   BGRA → libx264rgb -qp 0 (sin pérdidas) → ~grabando.nut
                 (áreas > 1920×1200×60 px/s: NV12 4:2:0 convertido en la GPU → libx264 CRF 8 / HW)
Pasada final (Máxima/Cine): .nut + .f32 → MP4
  Máxima: yuv420p + libx264 CRF 14 + AAC 256k
  Cine:   escalado ×2 «vecino más cercano» → yuv420p → libx264 CRF 10 + AAC 320k
          micrófono: paso alto 70 Hz + ganancia a −16 LUFS + limitador; dos fuentes mezcladas (amix)
```

### Puntos donde se perdía información en Cine

| # | Punto | Pérdida | Tipo |
|---|---|---|---|
| P1 | Pasada final `format=yuv420p` | croma submuestreado 4:2:0 (compensado a medias con el ×2) y conversión RGB→YCbCr de 8 bits | pérdida de datos |
| P2 | `libx264 -crf 10` | cuantización | pérdida de datos |
| P3 | `-c:a aac -b:a 320k` | códec perceptual | pérdida de datos |
| P4 | Micrófono: paso alto, ganancia, limitador, mezcla | muestras alteradas | transformación |
| P5 | Áreas grandes → NV12 en la GPU | 4:2:0 y rango limitado ya en vivo | pérdida de datos |
| P6 | `FramePool` agotado → el hilo de captura se bloquea; al recuperarse, los cortes atrasados eligen imagen de un historial de 6 (≈100 ms) | fotograma con la imagen equivocada, sin aviso | temporal, silenciosa |
| P7 | `source.Read` falla → se repite el fotograma anterior; GPU perdida → negro o repetición | sin contador | silenciosa |
| P8 | DXGI `AccumulatedFrames > 1` (imágenes fusionadas antes de recibirlas), `ProtectedContentMaskedOut` | no se miraban | silenciosa (origen) |
| P9 | Deriva del reloj de audio corregida interpolando | valores de muestra nuevos | transformación |
| P10 | Cambio de resolución/rotación durante la grabación | la duplicación se rehace con la geometría vieja | silenciosa |
| P11 | La pasada final borra el intermedio sin comprobar el resultado | ningún control de integridad | fallo silencioso posible |

Sin pérdida ya en v2.0.0: la copia DXGI (bytes BGRA del compositor), el dibujo del cursor (mismo resultado que en
pantalla), `libx264rgb -qp 0` del intermedio y los `.f32` de audio (salvo P9).

## 2. Evaluación de códecs y contenedores

Mediciones con el FFmpeg de Stackshot, entrada `bgr0` cruda a 60 fps, 240 fotogramas (4 s), en este equipo.
«UI» = capturas reales de Stackshot apiladas, 2 s desplazándose a 900 px/s y 2 s quietas; «complejo» =
`mandelbrot` a pantalla completa (peor caso). La lectura del archivo de entrada ya cuesta ~6,5 ms/fotograma a 1080p
(153 fps sin codificar), así que las cifras son cotas inferiores comparables entre sí.

| Códec (sin pérdidas) | 1080p UI | 1080p complejo | 1440p UI | 2160p UI | Tamaño 1080p UI / complejo |
|---|---|---|---|---|---|
| FFV1 nivel 3, rice, 24 slices, 12 hilos | 90 fps | **44 fps** | 70 fps | 32 fps | 42 MB / 351 MB |
| FFV1 nivel 3, range coder | 73 fps | 36 fps | — | — | 32 MB / 315 MB |
| UT Video | 91 fps | 86 fps | — | — | 208 MB / 475 MB |
| **H.264 RGB `libx264rgb -preset ultrafast -qp 0`** | **124 fps** | **82 fps** | 73 fps | 35 fps | **3 MB** / 297 MB |

- FFV1 no sostiene 60 fps a 1080p con contenido complejo en este portátil: la grabación tendría que parar o
  descartar fotogramas. H.264 sin pérdidas (perfil High 4:4:4 Predictive, planos GBR, `qp 0` = derivación de la
  transformada) es más rápido y, en contenido de pantalla, hasta 14× más pequeño gracias a la predicción entre
  fotogramas. Ninguno de los dos llega a 60 fps en 4K en este equipo.
- Ida y vuelta comprobada (prueba de concepto): 60 fotogramas `mandelbrot` 1080p `bgr0` → `libx264rgb -qp 0` → NUT →
  remux a MKV → decodificado en `gbrp`: los 60 MD5 por fotograma (`framemd5`, ambos lados en `gbrp`) son idénticos.
  Con `libx264 -qp 0 -pix_fmt yuv444p` (YCbCr) ya no lo son: la conversión RGB→YCbCr de 8 bits pierde.

### Compatibilidad (prueba empírica con el decodificador de Windows)

Miniatura del Explorador (`IShellItemImageFactory`, `SIIGBF_THUMBNAILONLY`, usa Media Foundation):

| Archivo | Resultado |
|---|---|
| H.264 4:2:0 en MP4 | miniatura OK |
| H.264 4:4:4 (YCbCr, qp 0) en MP4 | falla 0x8004B200 |
| H.264 RGB sin pérdidas en MP4 | falla 0x8004B200 |
| H.264 RGB sin pérdidas en MKV | falla 0x8004B200 |
| FFV1 en MKV | falla 0x8004B200 |

Ningún formato sin pérdidas lo decodifica Windows por sí solo; sí VLC, mpv, MPC-HC, Kdenlive, Shotcut, DaVinci
Resolve (H.264 4:4:4) o cualquier herramienta basada en FFmpeg. Un `.mp4` sin pérdidas tendría la extensión que
todos asocian a «se reproduce en cualquier sitio» y no se reproduciría en el reproductor de Windows ni en
navegadores, y además MP4 no admite bien PCM en coma flotante. Decisión: **Matroska (`.mkv`)**, contenedor abierto
que admite H.264 4:4:4, FFV1 y PCM float, con CRC32 en sus elementos. (Desde §16 el propio Stackshot lo reproduce.)

### Decisión

| | Elegido | Motivo |
|---|---|---|
| Contenedor | Matroska `.mkv` | sin ambigüedad de compatibilidad; PCM float; CRC32 |
| Vídeo | H.264 sin pérdidas en RGB (`libx264rgb -qp 0`), almacenado `gbrp` | sin pérdidas demostrado; el único medido que sostiene 1080p60 en el peor caso aquí |
| Formato de entrada | `bgr0` (B, G, R + byte ignorado) | el canal alfa del escritorio no significa nada (el escritorio es opaco y DXGI no lo garantiza); declararlo como relleno evita cualquier conversión |
| Audio | PCM float 32 bits 48 kHz estéreo, copiado tal cual | sin pérdidas respecto a lo que entrega WASAPI |
| FFV1 | evaluado, descartado como máster en vivo | no sostiene tiempo real en el peor caso |
| MP4 sin pérdidas | descartado | Windows no lo decodifica; MP4 queda como exportación derivada (con pérdida) desde el editor |

## 3. Arquitectura final de Cine

```
Recorder.Begin ── Cine.Preflight (hilo de interfaz, antes de nada)
   · FFmpeg sin libx264rgb/bgr0          → aviso, no se graba
   · < 2 GB libres en la unidad temporal → aviso, no se graba
   · área × 60 fps > 1920×1200×60 px/s   → Sí: 30 fps sin pérdidas · No: probar 60 fps · Cancelar
ScreenSource (DXGI, BGRA; nunca NV12 en Cine; historial de hasta 16 imágenes, ≤ 256 MB de GPU)
   hilo de recogida: AcquireNextFrame(0) bajo el candado `gpu`, siesta de 1 ms entre intentos
   contadores: SourcePresents, Coalesced, Presents, Unused, Misses, Protected, LostEvents/LostMs, GeometryChanged, Hdr
Hilo de fotogramas (igual que antes: corte por fotograma, cursor del instante)
   cola: FramePool.TryTake (sin esperar) · hasta min(2 GiB, ¼ de la RAM libre) · 8–600 fotogramas
   cola llena / > 1 s de retraso / poco disco / cambio de geometría → Halt(motivo): se para y se conserva lo grabado
Hilo escritor → CineSink: copia el fotograma, calcula su hash, lo escribe entero en la tubería
   ffmpeg -f rawvideo -pix_fmt bgr0 -i - -c:v libx264rgb -preset ultrafast -qp 0 -g 2s
          -x264-params colorprim=bt709:transfer=iec61966-2-1:colormatrix=GBR:range=pc -f nut ~grabando….nut
AudioCapture (modo exacto) → ~grabando … equipo.f32 / micro.f32
Al parar: CineSink.Close → audio recortado/rellenado a round(fotogramas × 48000 / fps)
   CineMaster.Build: remux (-c:v copy, -c:a copy) → ~cine <fecha>.mkv
   CineVerifier.Verify (sondeo framecrc + decodificación completa)  → PASS: «Cine <fecha>.mkv»
                                                                    → FAIL: «Cine <fecha> (sin verificar).mkv» + aviso
   cualquier fallo después de grabar → los .nut/.f32 se mueven a %LOCALAPPDATA%\Stackshot\recuperado\<fecha>
   informe JSON → %LOCALAPPDATA%\Stackshot\cine\<nombre>.json (+ resumen en stackshot.log)
```

Estándar, Alta y Máxima no pasan por nada de lo anterior salvo `ScreenSource`, cuyos arreglos (§6) les benefician
por igual; sus argumentos de FFmpeg se construyen ahora con funciones puras (`Recorder.PlanLive`, `FinishArgs`,
`GifArgs`) que reproducen byte a byte los de v2.0.0 (§9.1).

### Parámetros efectivos de FFmpeg

| Paso | Argumentos |
|---|---|
| En vivo | `-hide_banner -loglevel error -y -f rawvideo -pix_fmt bgr0 -video_size W×H -framerate N/D [-skip_initial_bytes n] -i - -map 0:v -c:v libx264rgb -preset ultrafast -qp 0 -g round(2·fps) -x264-params colorprim=bt709:transfer=iec61966-2-1:colormatrix=GBR:range=pc -f nut "~grabando ….nut"` |
| Máster, sin sonido | `-i nut -map 0:v -c:v copy -metadata comment=… -f matroska "~cine ….mkv"` |
| Máster, una fuente | `… -f f32le -sample_rate 48000 -ch_layout stereo -i f32 -map 0:v -c:v copy -map 1:a -c:a copy -metadata:s:a:0 title=… -disposition:a:0 default …` |
| Máster, dos fuentes | pista 0: mezcla (`highpass` 70 Hz + ganancia de voz + `amix normalize=0` + `alimiter`) en `pcm_f32le`, por defecto; pistas 1 y 2: originales con `-c:a copy` |
| Sondeo | `-hide_banner -nostdin -i master -map 0 -c copy -f framecrc -` |
| Vídeo decodificado | `-i master -map 0:v:0 -f rawvideo -pix_fmt gbrp -` (el flujo ya es `gbrp`: no hay conversión) |
| Sonido decodificado | `-i master -map 0:a:k -c:a pcm_f32le -f f32le -` |

`-skip_initial_bytes` solo aparece cuando la consola del proceso tiene una codificación con preámbulo (BOM UTF-8): .NET
Framework lo escribe en la tubería al arrancar el proceso. Nunca ocurre en la app (WinExe); sí en herramientas de consola.

## 4. Contrato de fidelidad

**Vídeo.** Referencia: cada fotograma que Stackshot entrega a FFmpeg, es decir, la imagen del escritorio que DXGI
entrega en BGRA de 8 bits, recortada al área, con el cursor dibujado encima, en formato `bgr0`. Garantía: el archivo
contiene exactamente esos fotogramas, en el mismo número y orden, y cada píxel decodificado tiene los mismos valores
R, G y B. El cuarto byte (alfa) no forma parte de la garantía: en el escritorio no significa nada y DXGI no lo fija.

Comprobación: un hash de 64 bits por fotograma, calculado en `CineSink` sobre los bytes exactos que se escriben en la
tubería (B | G≪8 | R≪16 por píxel con el cuarto byte a cero, dos píxeles por palabra, mezcla multiplicativa de tipo
xxHash), frente al mismo hash calculado sobre los planos `gbrp` que devuelve el decodificador, sin conversión
intermedia (y una conversión en ese lado solo podría crear diferencias, nunca ocultarlas). La probabilidad de que una
diferencia accidental deje el hash igual es del orden de 2⁻⁶⁴ por fotograma. Las pruebas `roundtrip` añaden una
comparación independiente con `framemd5` de FFmpeg, que no usa el hash de Stackshot.

**Sonido.** Referencia: las muestras de 48 kHz estéreo en coma flotante de 32 bits que Stackshot coloca en la línea de
tiempo de la grabación (el `.f32`). Garantía: las pistas «(original)» del máster tienen esos bits exactos (SHA-256) y
esa longitud exacta. La colocación en la línea de tiempo sí puede insertar silencio (huecos, o nada sonando), descartar
paquetes solapados o repetir/quitar una muestra para seguir la deriva del reloj de la tarjeta; en Cine nunca
interpola (modo exacto) y todo queda contado en el informe. La pista «Mezcla» es una derivada procesada y está fuera
de la garantía. Antes de llegar a Stackshot, Windows puede convertir el formato del dispositivo a 48 kHz estéreo
float (`AUTOCONVERTPCM`); si el dispositivo lo rechaza y convierte Stackshot, el informe lo dice.

**Tiempo.** Frecuencia constante (60, 59,94, 30 o 29,97 fps según la pantalla y lo aceptado); en Matroska cada marca
va redondeada al milisegundo (se comprueba a ±1 ms + 1 tb de i·D/N, siempre hacia delante). Que una imagen estática
se repita en varios fotogramas no es pérdida.

**Lo que no se garantiza (y se dice).**
- El color que ve el usuario: Windows entrega valores sRGB de 8 bits; perfiles ICC, luz nocturna o la calibración
  del monitor no forman parte de la imagen capturada. Con el escritorio en HDR, Windows convierte a SDR antes de
  entregarla (se detecta y se avisa; no se ha podido probar sin pantalla HDR).
- Cada refresco físico: a 60 fps en una pantalla de 144 Hz se graba una de cada ~2,4 imágenes. El informe cuenta las
  que no aparecen en ningún fotograma (`imagesUnused`) y las que Windows fusionó antes de entregarlas (`coalesced`).
- Ventanas protegidas o excluidas de la captura: Windows las oculta (también las de Stackshot); `protectedContent`
  cuenta las imágenes afectadas en todo el monitor, no solo en el área.

## 5. Captura

Se mantiene Desktop Duplication (DXGI 1.2/1.5) con GDI de reserva: ya era la API de Stackshot, entrega la imagen del
compositor sin lecturas GDI y con la hora de presentación de cada imagen, y su BGRA de 8 bits se copia sin
transformación. Windows Graphics Capture no aportaría fidelidad (es la misma imagen del compositor) y sí otra
dependencia (WinRT). Monitor, ventana y región siguen siendo un rectángulo del escritorio, como antes. Cine nunca usa
la conversión NV12 por GPU (4:2:0): las áreas grandes se graban en BGRA a 60 fps o, si el usuario lo acepta, a 30.

## 6. Defectos encontrados y corregidos en la captura (afectan a todos los perfiles)

1. **Movimiento hacia atrás** (`Duplication.cs`). El hilo de recogida llamaba a `AcquireNextFrame`/`ReleaseFrame` sin
   el candado `gpu` mientras el hilo de fotogramas usaba el contexto inmediato de Direct3D 11, que no es seguro entre
   hilos. Medido con un patrón que cambia en cada composición: 1–3 fotogramas cada 6 s mostraban una imagen ~9
   refrescos más antigua que la del fotograma anterior, con todos los píxeles de una imagen real (ninguna comprobación
   de píxeles lo veía). Aislamiento: la misma ventana capturada con GDI (0 en ~1.370 muestras) o con un bucle DXGI
   mínimo de un solo hilo (0 en 3.346) no lo mostraba; `ScreenSource` sí, incluso eligiendo siempre la imagen más
   reciente. Esperar a la GPU antes de `ReleaseFrame` no lo arreglaba; serializar, sí. Arreglo: todas las llamadas de
   duplicación bajo `gpu` sin esperar dentro (`AcquireNextFrame(0)` + 1 ms de siesta) y el contexto con
   `ID3D10Multithread::SetMultithreadProtected`. Coste: el hilo de recogida gasta un 8,1 % de un núcleo con el
   escritorio en movimiento, frente al 13,6 % de antes (misma zona, 10 s).
2. **Historial corto a 144 Hz.** Las 6 imágenes guardadas cubren ~40 ms en esta pantalla y el hilo de fotogramas llega
   a ir 25–43 ms tarde en grabaciones normales: un fotograma podía quedarse sin su imagen (se usaba la más antigua
   guardada, sin aviso). `Pick` ahora lo cuenta (`historyMisses`, también en los demás perfiles) y Cine guarda hasta 16
   imágenes (≤ 256 MB de GPU); el resto de perfiles conserva 6.
3. `Preparando2026` en la barra de grabación (escape mal convertido) → «Preparando…»; y `C00E1mara` en el registro.

## 7. Cola, contrapresión y política de fallos

| Situación | Antes | Cine ahora |
|---|---|---|
| Codificador más lento que la pantalla | el hilo de captura se bloquea; al recuperarse, fotogramas con imágenes equivocadas | cola en RAM (hasta 2 GiB); llena → se para, conserva y verifica lo grabado y explica el motivo |
| Hilo de fotogramas > 1 s tarde | sigue, con imágenes equivocadas | se para y conserva |
| Imagen ya sobrescrita (< 1 s tarde) | sin aviso | `historyMisses` + aviso al terminar |
| Lectura de GPU fallida | repite el anterior sin aviso | igual, contado (`framesRepeatedFailed`) y avisado |
| Poco disco (< .nut × 1,05 + sonido + 512 MB) | FFmpeg falla | se para y conserva |
| Cambio de resolución u orientación | sigue con la geometría vieja | se para y conserva |
| FFmpeg muere | error | error + archivos de trabajo a `recuperado\` |
| Fallo al crear el máster | error, se borra todo | error + archivos de trabajo a `recuperado\` |
| Verificación fallida | — | se guarda como «(sin verificar)» con el detalle |
| Descartar (Esc / Descartar) | se borra todo | igual (lo pide el usuario) |
| Procesos FFmpeg | — | ninguno huérfano en todas las pruebas (comprobado por PID padre) |

Nunca se cambia de códec, se reduce la resolución ni se baja la frecuencia sin que el usuario lo acepte.

## 8. Matriz de pruebas

| Suite | Qué cubre |
|---|---|
| `regression` | 5.980 escenarios de argumentos de Estándar/Alta/Máxima/GIF (1280×720…3840×2160 y 1000×2000, NV12 sí/no, 4 codificadores de GPU, 4 frecuencias, 4 combinaciones de sonido, ganancia de voz) + `FinalCodec`/`AudioCodec`, contra la referencia de v2.0.0 |
| `hash` | alfa ignorado; un byte B/G/R cambiado en cualquier píxel; píxeles intercambiados; dimensiones; `Bgr0(IntPtr)` = `Bgr0(byte[])` = `Gbrp`; nº impar de píxeles; implementación de referencia independiente |
| `args` | Cine en vivo y máster sin `-vf`, `scale`, `format=`, `yuv`, `crf`, `aac`, `nv12`; pistas y títulos; `Gop`, `AudioFramesFor`, `PoolFrames` |
| `pool` | `TryTake` hasta la capacidad, devolución, `InUse` |
| `roundtrip` | pérdida cero: texto con colores de sintaxis, líneas de 1 px, degradados con los 256 niveles por canal, barras y tablero, negro, blanco, ruido, movimiento y desplazamiento, basura en el 4.º byte; 1280×720, 1920×1080, 2560×1440, 3840×2160, 1000×2000, 16×16; 60, 59,94 y 30 fps; sin sonido, sistema, micro y ambos (con ±1, 0, −0 y subnormales); verificador + `framemd5` independiente + SHA-256 del sonido |
| `negative` | el verificador falla con: un hash alterado (índice exacto), uno de menos, máster truncado al 70 %, el mismo vídeo con pérdida (`yuv444p crf 0`, `yuv420p crf 10`), FFV1 (píxeles idénticos, códec distinto), ancho erróneo, una muestra de sonido cambiada, archivo inexistente |
| `audio` | modo exacto: continuo, hueco de 100 ms, solape de 50 ms, silencio inicial, relleno y recorte finales, deriva ±0,1 % (dentro de rango) y ±0,5 % (fuera: resincroniza); cada fotograma de salida es un fotograma de entrada entero o silencio, en orden; el modo antiguo se mide (interpola) |
| `failures` | FFmpeg inexistente, FFmpeg matado a mitad, sin procesos hijos tras `Dispose`, máster sin `.nut`, carpeta inexistente, archivo vacío |
| `stress` | 20 idas y vueltas aleatorias (tamaño, patrón, fps, sonido) y 3 min de 1080p60 (memoria) |
| `bench` | velocidad, tamaño, CPU y memoria de FFmpeg por resolución y contenido |
| `realcapture` | captura DXGI real de una ventana con patrón conocido: fidelidad píxel a píxel y orden (5 × 6 s) |
| `e2e\run-e2e.ps1` | grabaciones reales con `ShotStack` + `Recorder`: Cine sin sonido, con sonido del equipo, 60 s a 720p, sobrecarga (FFmpeg suspendido 14 s), descarte; Estándar, Alta y Máxima; cada fotograma decodificado se compara con la imagen que su id debe tener |
| `e2e\run-e2e.ps1 -Probes` | orden: `ScreenSource` (más reciente / como una sesión), DXGI mínimo y GDI |

No se ha podido probar en este equipo (falta de hardware o intervención del usuario): varios monitores, cambio de
monitor, de DPI o rotación durante la grabación, pantalla HDR, cambio de tarjeta gráfica, GDI como captura principal
(solo como sonda), micrófono real (por privacidad: el camino se probó con datos sintéticos; el sonido del equipo sí con
el dispositivo real), dispositivos de sonido que desaparecen, disco lleno de verdad, CPU o GPU saturadas por otra
carga, sesiones de horas. El código de esos casos existe (detección de geometría, `LostEvents`, reserva de disco, paso
a GDI), pero no hay evidencia en este equipo.

## 9. Resultados (código final, 09/10/2026, este equipo)

### 9.1 Regresión de Estándar, Alta y Máxima

- Referencia fijada **antes** de tocar el código: `tests\cine\LegacyBaseline.cs` es una copia literal de la lógica de
  v2.0.0 (commit a7bf44f) y generó `baseline-profiles.txt` (5.980 escenarios, SHA-256
  `3FD46EE3…B60C3157`). El código final reproduce los 5.980 escenarios byte a byte (`regression`).
- Grabaciones reales (`e2e`) con Estándar (30 fps), Alta y Máxima (60 fps) y sonido del equipo: MP4 decodificable
  entero, fotogramas en orden, sin procesos ni archivos de trabajo sobrantes.
- Lo único que cambia para ellos es la captura (§6): mismo resultado, sin fotogramas hacia atrás y con menos CPU.

### 9.2 Pruebas automáticas

| Ejecución | Resultado |
|---|---|
| `regression hash args pool roundtrip negative audio failures realcapture` | **71 PASS, 0 FAIL** (`results\20261010-003706`, después de las correcciones de §15) |
| `stress` (20 idas y vueltas aleatorias + 3 min de 1080p60, 6.594 fotogramas verificados) | 21 PASS; memoria privada estable en 51 MB |
| `bench` | 12 PASS (cifras en §10) |
| Detector de orden contra el código **sin** el arreglo (`-SrcRoot` a una copia) | FAIL: 8–16 fotogramas hacia atrás por 1.800, dos tandas |
| Detector de orden contra el código final | PASS: 0 de 1.800 (sesión) y 0 de 1.800 (más reciente), dos tandas |

### 9.3 Grabaciones reales con la app (`e2e`, ventana de patrón a 144 Hz)

| Escenario | Fotogramas | Idénticos al patrón | Hacia atrás | Imagen equivocada | Verificación | Notas |
|---|---|---|---|---|---|---|
| Cine sin sonido, 640×360, 10 s | 592 | 592 | 0 | 0 | PASS | retraso máx. 19 ms, cola máx. 2 |
| Cine + sonido del equipo, 10 s | 588 | 588 | 0 | 0 | PASS | sonido: 470.400 muestras exactas, bits idénticos (SHA-256) |
| Cine 1280×720, 60 s, sonido | 3.588 | 3.588 salvo el puntero* | 0 | 0 | PASS | 122 MB; sonido 2.870.400 muestras, bits idénticos; verificar 28,1 s; remux 0,7 s |
| Cine con FFmpeg suspendido 14 s | 726 | 726 | 0 | 0 | PASS | se detuvo sola: «El equipo no ha podido codificar sin pérdidas al ritmo de la pantalla»; cola 600/600; 2 fotogramas tomados no escritos (avisado) |
| Cine, Descartar a los 5 s | — | — | — | — | — | nada guardado, ni procesos ni archivos |

\* El puntero del ratón, parado sobre la ventana, ocupa los mismos 14×21 px en todos los fotogramas: Stackshot lo
dibuja a propósito. Todo lo demás, idéntico.

Comprobación de la imagen de origen: en todas las grabaciones Cine cada fotograma decodificado es **píxel a píxel** la
imagen que la ventana dibujó para ese número de fotograma, así que la cadena completa (DWM → DXGI → Stackshot →
FFmpeg → MKV → decodificador) no altera ni un valor.

## 10. Rendimiento

Codificación sin pérdidas (`CineSink` a tope de velocidad, 240 fotogramas; `bench`):

| Contenido | 1280×720 | 1920×1080 | 2560×1440 | 3840×2160 |
|---|---|---|---|---|
| Interfaz desplazándose | 130 fps · 14 MB/s | 84 fps · 18 MB/s | 52 fps · 14 MB/s | 17 fps · 2 MB/s |
| Estática | 133 fps · 5 MB/s | 93 fps · 7 MB/s | 52 fps · 7 MB/s | 17 fps · 5 MB/s |
| Ruido (peor caso imposible) | 70 fps · 287 MB/s | 18 fps · 164 MB/s | 8 fps · 130 MB/s | 4,5 fps · 167 MB/s |
| Memoria máx. de FFmpeg (interfaz / ruido) | 162 / 291 MB | 324 / 691 MB | 544 / 1.198 MB | 1.158 / 2.651 MB |

Lectura: en este portátil Cine sostiene 60 fps hasta 1080p con contenido de pantalla real; 1440p queda en ~52 fps y
4K en ~17 fps, por eso esas áreas preguntan antes (30 fps) y, si aun así no llega, la grabación se para y conserva lo
hecho. El ruido puro (cada píxel distinto en cada fotograma) no es comprimible sin pérdidas en tiempo real a 1080p.

Otros costes medidos:
- Hash por fotograma (1080p): 2,2 ms al escribir y 2,4 ms al verificar; un hilo.
- Guardado: el remux tarda < 1 s; la verificación decodifica todo el archivo (≈128 fps a 720p, 28 s para 60 s de
  vídeo). A 1080p cuenta con un tiempo parecido a lo que duró la grabación.
- Tamaño: 122 MB por minuto a 720p con animación continua; de pocos MB a varios GB por minuto según el movimiento.
- Captura: hilo de recogida 8,1 % de un núcleo; retraso máximo del hilo de fotogramas 18–36 ms; cola 1–3 fotogramas
  con el codificador al día.
- Estabilidad: 3 min de 1080p60 con la memoria privada plana (51 MB).

No se midió la CPU total del proceso durante una grabación larga ni el uso de GPU (no hay herramienta en el equipo sin
instalar nada).

## 11. Limitaciones pendientes

- Solo probado en este equipo (una pantalla de 144 Hz, SDR, una GPU). Varios monitores, HDR, rotación, cambios de
  DPI o de tarjeta durante la grabación y micrófono real están implementados o detectados pero sin evidencia.
- La verificación es secuencial y tarda del orden de la duración del vídeo a 1080p; se podría paralelizar el hash.
- 1440p y 4K no llegan a 60 fps sin pérdidas en este equipo (límite del codificador por CPU; no hay codificador por
  GPU sin pérdidas en RGB).
- El reproductor de Windows no decodifica H.264 4:4:4/RGB. Resuelto en §16: Stackshot reproduce el máster por sí
  mismo (`docs/cine-playback.md`); VLC o mpv ya no hacen falta.
- El reloj de sonido se corrige hasta un 0,21 % de deriva sin huecos; más allá se resincroniza con silencio o descarte
  (contado). Los dispositivos reales derivan en torno al 0,01 %.
- Al cerrar Stackshot a mitad de una grabación Cine, FFmpeg termina el `.nut` (recibe el fin de la tubería) pero no se
  crea el máster; los archivos de trabajo quedan en `temp` y la limpieza normal los borra pasado el tiempo configurado.

## 12. Reproducción

```powershell
cd C:\Projects\stackshot
.\build.ps1                                   # la app
.\tests\cine\run-tests.ps1                    # suites rápidas (~3 min)
.\tests\cine\run-tests.ps1 realcapture        # abre una ventana de 640×360 abajo a la derecha (~70 s)
.\tests\cine\run-tests.ps1 stress bench       # ~6 min, varios GB temporales
.\tests\cine\e2e\run-e2e.ps1                  # grabaciones reales abajo a la izquierda (~3 min)
.\tests\cine\e2e\run-e2e.ps1 -Probes          # sondas de orden
.\tests\cine\run-tests.ps1 -SrcRoot <copia de src> realcapture   # el detector contra otro código
```

Comprobar a mano un máster: `ffmpeg -i "Cine ….mkv" -map 0 -c copy -f framecrc -` (estructura y tiempos) y
`ffmpeg -i "Cine ….mkv" -map 0:v -f framemd5 -` (hash por fotograma). El informe de cada grabación está en
`%LOCALAPPDATA%\Stackshot\cine\<nombre>.json`.

## 13. Cambios de distribución y dependencias

Ninguna dependencia nueva: el mismo FFmpeg 9.0.2 que ya descarga Stackshot (con `libx264rgb`, comprobado antes de
grabar). Extensión nueva `.mkv` para Cine (los demás perfiles siguen en `.mp4`/`.gif`); la lista de recientes incluye
`.mkv`, y como el Explorador no sabe hacer miniaturas de estos archivos, Stackshot guarda en memoria una del propio
vídeo. Carpetas nuevas en `%LOCALAPPDATA%\Stackshot`: `cine\` (informes) y `recuperado\` (solo si algo falla; nunca se
limpia sola).

## 14. Relevos entre modelos (lo que hizo cada uno de verdad)

- **Opus 5.5** (sesión principal): auditoría, mediciones de códecs y compatibilidad, referencia de regresión,
  especificación, revisión del código, arnés extremo a extremo, diagnóstico y arreglo de la captura (§6: el contador
  `historyMisses` que no contaba, el desorden de fotogramas, el historial corto), política de retraso, documentación.
- **Sonnet 5.5** (subagente): implementación según la especificación (`Cine.cs`, rama Cine de `Session`, contadores,
  modo de sonido exacto, refactor de argumentos, miniaturas, textos). Detectó y resolvió el BOM en la tubería.
- **Haiku 5.5** (subagente, sin ver la implementación): batería `tests\cine` escrita contra la especificación, tres
  rondas; encontró los límites de la corrección de deriva, la debilidad de su propio detector de orden (corregida y
  validada contra el código sin arreglar) y midió el rendimiento.

## 15. Segunda verificación (10/10/2026, a petición del usuario)

### 15.1 Pruebas nuevas, con la app real

| Prueba | Resultado |
|---|---|
| Cine a **pantalla completa 1920×1080**, 30 s, patrón animado en toda la pantalla a 144 Hz | 1.788 fotogramas idénticos al patrón (salvo el puntero), 0 hacia atrás, captura limpia, PASS |
| Ídem con **sonido del equipo + micrófono real**, dos veces | 1.788 y 1.782 fotogramas; captura limpia, PASS; la cola llegó a 183 y 106 de 258 |
| Ídem, primera vez (antes de las correcciones) | se detuvo sola a los 13,8 s («no ha podido codificar…»), cola 258/258; los 828 fotogramas guardados, idénticos y verificados |
| Cine con **tu escritorio real**, 20 s, sonido del equipo + micrófono | 1.202 de 1.202 fotogramas idénticos; 3 pistas; equipo y micrófono **bit a bit** (SHA-256); 961.600 muestras exactas; captura limpia; el vídeo se borró al terminar |
| **Exportar a MP4** desde un máster (mismos argumentos que el editor para Cine) | H.264 High 4:2:0 + AAC; Windows lo decodifica (miniatura OK); el máster, no (como se esperaba) |

Conclusión sobre la carga: con contenido normal de escritorio, Cine a 1080p60 va sobrado (cola de 3 fotogramas). Con
animación continua en toda la pantalla —el patrón de prueba se redibuja a 1920×1080 144 veces por segundo y él solo
ocupa más de un núcleo— está **en el límite** de este portátil: dos de tres pasadas aguantaron 30 s y una se detuvo a
los 13,8 s conservando lo grabado. La tubería no es el cuello de botella (2,2 GB/s medidos, ~280 imágenes 1080p/s) ni
hay alternativa en disco (411 MB/s de escritura, ~52 imágenes 1080p/s sin comprimir): el límite es la CPU del
codificador sin pérdidas compartida con la aplicación que se graba.

### 15.2 Revisión de código independiente (otro agente, sin participar en la implementación)

Confirmó correctos el camino del hash, el verificador, el modo exacto del sonido, la política de no degradar y los
perfiles antiguos. Encontró defectos en los caminos de error, todos corregidos:

1. Si fallaba el rescate, se borraban los archivos de trabajo → `Rescue` mueve cada archivo por separado; el que no
   se puede mover se queda donde está y ya nunca se borra; el mensaje lista dónde está cada uno.
2. Una verificación fallida borraba el `.nut` (la otra copia sin pérdidas) → ahora también se conserva en
   `recuperado\`, junto al aviso.
3. `CineSink` no se liberaba al terminar (8 MB fijados en memoria por grabación a 1080p) → se libera siempre.
4. El aviso «sin pérdidas, verificado» salía aunque hubiera HDR, captura GDI, interrupciones de la captura o sonido
   convertido → esos casos ya muestran el informe completo.
5. El vaciado final de la cola podía esperar para siempre con FFmpeg atascado → como mucho 10 s; lo que no entra
   cuenta como descartado.
6. La decodificación de comprobación dejaba a FFmpeg ajustar la frecuencia (solo podía causar un FALLO falso) →
   `-an -fps_mode passthrough`.
7. Dos liberaciones de DXGI fuera del candado y la protección multihilo pedida al contexto → bajo el candado y pedida
   al dispositivo (con registro si falta).
8. El máster ya hecho se quedaba oculto si algo fallaba al moverlo, y la miniatura no se liberaba en los errores →
   corregido.
9. FFmpeg de la verificación podía quedarse bloqueado si la lectura fallaba → se cierra siempre.

### 15.3 Defecto encontrado en esta verificación: grabación en negro con el escritorio protegido

Una grabación de prueba coincidió con un momento en que Windows mostraba un escritorio seguro (pantalla de bloqueo o
aviso de UAC): DXGI respondió `E_ACCESSDENIED`, Stackshot pasó a la captura GDI de reserva y grabó negro, y el informe
lo daba por limpio. Corregido de tres maneras: Cine no empieza si DXGI deniega el acceso (lo explica), la captura GDI
cuenta como fallida una copia que Windows rechaza (en todos los perfiles: se repite el fotograma anterior en vez de
uno de hace tres), y una grabación Cine que haya usado GDI ya no se presenta como limpia.

### 15.4 Lo que sigue sin probar

Lo mismo que en §8 y §11: varios monitores, HDR, rotación o DPI durante la grabación, cambio de tarjeta, disco lleno
de verdad, dispositivos de sonido que desaparecen y sesiones de horas. El caso del escritorio protegido se ha visto
una vez por casualidad, pero la corrección no se ha podido provocar a propósito (habría que bloquear tu sesión).

## 16. Reproducción integrada, exportación MP4 y edición con varios vídeos (10/10/2026)

Detalle en `docs/cine-playback.md` y `docs/cine-share-export.md`. Resumen:

| Requisito | Estado |
|---|---|
| Reproducir el máster dentro de Stackshot sin convertirlo (pausa, saltos, volumen, pistas, liberar recursos) | PASS (40 pruebas del motor + 13 de casos límite; 0 FFmpeg vivos al cerrar) |
| Ventana de reproductor con ajustes (velocidad, repetir, fotograma a fotograma, pantalla completa, información, escalado) | PASS por pruebas automáticas; clics reales NO VERIFICADO |
| Abrir cualquier extensión de vídeo (38 conocidas + sondeo por contenido) | PASS (14 contenedores reproducidos hasta el final) |
| Saltos exactos en contenedores sin índice (TS/MPG/M2TS) | PASS (≤1 fotograma; antes, hasta un GOP tarde) |
| Exportación MP4 H.264 High/yuv420p/AAC-LC/faststart, BT.709, con comprobación antes de presentarla | PASS |
| Windows decodifica el MP4 exportado | PASS (Media Foundation, entero) |
| Calidad Máxima/Alta/Ligera con tope de tasa seguro | PASS |
| Añadir vídeos en el editor y exportarlos juntos manteniendo la calidad | PASS (clips de distinto tamaño, fps, formato y color) |
| Ventana de opciones antes de grabar y cuenta atrás de 3 s | PASS (e2e: 10 s pedidos, 7,15 s grabados tras 3 s de cuenta atrás, verificado) |
| Estándar/Alta/Máxima sin regresión | PASS (referencias de argumentos y hash) |
| Instalación limpia en otro equipo | NO VERIFICADO |

Defectos encontrados en esta ronda y corregidos: velocidad 2× que exportaba a la mitad de fps; sonido mono rechazado
por la comprobación; alto de GIF redondeado distinto de FFmpeg; FLV que no daba imagen con `-ss 0`; `.mkv` cualquiera
enviado al reproductor con el ajuste apagado; extensiones desconocidas que acababan en el editor de imágenes; clip
BT.601 que cambiaba de color al unirlo; paleta de GIF exportado que perdía colores; editor que rechazaba WMV/FLV/MPG/TS
(lista blanca más corta que la del reproductor, ahora es la misma); saltos tardíos en TS/MPG; `-to` de FFmpeg que en
TS cuenta desde donde cae el salto (el final se corta ahora con `trim`).

Relevos: **Opus 5.5** especificó, revisó, integró, corrigió los defectos de formatos/saltos y midió; **Sonnet 5.5**
implementó el reproductor, la ventana, los clips y la calidad, y corrigió lo que encontró Haiku; **Haiku 5.5** escribió
pruebas independientes de casos límite (reproductor, formatos, clips) a partir de la especificación, leyendo el código
sin modificarlo. Haiku encontró los defectos de la lista desde la velocidad 2× hasta la paleta de GIF; los de la lista
blanca del editor, los saltos en TS/MPG y el `-to` los encontró Opus al revisar la integración.

## 17. Búsqueda de fallos en toda la aplicación (10/10/2026, tarde)

Cinco testers independientes (Haiku 5.5) por áreas —grabación, reproductor, editor de vídeo y exportación, editores
de imagen/Markdown/XML y el resto de la app— escribieron pruebas nuevas (`tests\*\*Hunt*.cs`) sin tocar el código;
Sonnet 5.5 corrigió y otro Haiku verificó desde cero. Opus revisó, corrigió lo que quedó y repitió las grabaciones
reales.

| Hallazgo | Gravedad | Estado |
|---|---|---|
| Desinstalar borraba capturas si la carpeta de guardado era la propia carpeta de datos | alta | corregido: sólo se borra lo de Stackshot |
| «Formatear» XML cambiaba saltos de línea y tabuladores dentro de atributos | media | corregido (se escriben como `&#xA;`/`&#x9;`) |
| Pixelado del editor de vídeo mostraba el mosaico del fotograma anterior | media | corregido |
| Máxima en áreas grandes (> 1920×1200 a 60 fps) se anunciaba «sin pérdidas» | media | texto corregido (es 4:2:0, ya documentado en P5) |
| Miniatura fallida por archivo bloqueado no se reintentaba en toda la sesión | media | corregido |
| Archivos sin duración en la cabecera (WebM del navegador, MKV interrumpido): barra sin duración, sin recortes, exportación «sin validar» | media | corregido (duración leída de los paquetes) |
| Prueba e2e que escribía informes y grabaciones en la carpeta real del usuario | media (pruebas) | corregido: carpeta privada `STACKSHOT_TEST_DATA`; los 11 informes de prueba se sacaron de `%LOCALAPPDATA%\Stackshot\cine` |
| La limpieza de temporales podía borrar un vídeo abierto en el reproductor | baja | corregido |
| «Abrir vídeo…» mandaba imágenes al reproductor | baja | corregido (van al editor) |
| fps fuera de 24/30/60 en ajustes: la ventana mostraba otro valor | baja | corregido |
| Ajustes: números desbordados, archivo en ANSI, nombre de mascota partiendo un emoji | baja | corregidos |
| Cuenta atrás: el primer cuadro mostraba «1» | baja | corregido |
| Búsqueda «palabra completa» con signos al borde; «Ordenar» Markdown no idempotente; cambio externo con igual tamaño y fecha | baja | corregidos |
| Tamaños «1024 KB» en el límite | baja | corregido |
| Tramo vacío o invertido en la exportación | baja (inalcanzable desde la interfaz) | ahora se rechaza con un error claro |
| Origen con duración de cabecera falsa (más larga que el contenido) | baja | se mantiene: la exportación correcta se marca «(sin validar)», porque no se distingue de una exportación truncada sin leer todo el origen |

Grabaciones reales tras las correcciones (e2e, área pequeña abajo a la izquierda): 9 de 11 a la primera; la cuenta
atrás falló una vez por historial perdido con el equipo cargado y pasó limpia en dos repeticiones (7,07 y 7,08 s de
10 s con 3 s de cuenta atrás); Cine a pantalla completa animada se detuvo por CPU en las dos pasadas (navegador con
vídeo y antivirus activos), conservando lo grabado y verificado: es la política prevista y el límite ya medido en
§15.1; el código de captura Cine no cambió en esta ronda (diff comprobado).

## 18. Auditoría de la exportación MP4 (11/10/2026)

Detalle, benchmark y pruebas en `docs/cine-share-export.md` (§3–§13). Se partió del MP4 real
`Cine 2026-10-11 17.03.26 (para compartir).mp4` y de su máster (copias; los originales no cambiaron). El MP4 no perdía
tamaño, fps, duración ni sonido, y los ajustes de antes lo reproducen imagen por imagen. Máxima (CRF 10) queda a
0,11 dB del techo de 4:2:0; CRF 14/16/18 pierden 0,14/0,25/0,41 dB, así que no se cambia.

| Defecto | Gravedad | Estado |
|---|---|---|
| El MP4 copiaba la descripción «sin pérdidas» del máster y el reproductor lo mostraba como máster sin pérdidas | alta | corregido (descripción propia; `IsMaster` exige RGB) |
| Nivel H.264 5.0 en toda exportación (6.1 en 4K60) | media | corregido: el nivel más bajo que admite el fotograma (4.1 hasta 1080p30) |
| Tope de 40 Mb/s que recortaba CRF 10 en desplazamientos rápidos (−1,4 dB de media, −3,2 dB el peor fotograma) | media | corregido: tope del nivel (62,5 Mb/s en 4.1) |
| Pista de sonido elegida ignorada al exportar | media | corregido |
| Vídeos de frecuencia variable que perdían fotogramas (2 de 90) | media | corregido (`-fps_mode passthrough`) |
| Comprobación que rechazaba vídeos de frecuencia variable correctos | media | corregido |
| Croma centrado sin indicarlo (−1,04 dB en reproductores que siguen la norma) | baja | corregido sin cambiar un píxel |
| El reproductor no comprobaba fps ni fotogramas de un máster; título de pista del máster en el MP4 | baja | corregido |

Estándar/Alta/Máxima de grabación sin cambios (5980 escenarios idénticos). 4:4:4 medido (+11,5 dB) y descartado como
formato para compartir: Windows no lo decodifica. Móviles y teles reales: NO VERIFICADO.
