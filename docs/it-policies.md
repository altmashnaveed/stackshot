# Línea de órdenes y directivas de empresa

Cómo lanzar capturas desde fuera de Stackshot (un botón del Logitech MX Keypad, un acceso directo, un script) y cómo
puede fijar ajustes el departamento de informática.

## Línea de órdenes

```
Stackshot.exe --capture <modo>
Stackshot.exe --open-home
Stackshot.exe --last-xml
```

| Opción | Qué hace |
|---|---|
| `--capture <modo>` | Lo mismo que el atajo de ese modo. |
| `--open-home` | Abre la ventana principal. |
| `--last-xml` | Abre en el visor el XML más reciente de la carpeta vigilada (Ajustes > General > Avanzado, o la que fije la directiva `XmlWatchFolder`). |

Modos de `--capture`:

| Modo | Acción |
|---|---|
| `region` | Capturar un área |
| `delayed` | Capturar un área con temporizador (3, 5 o 10 segundos, según Ajustes) |
| `repeat` | Repetir la última área |
| `screen` | Capturar la pantalla |
| `window` | Capturar la ventana activa |
| `scroll` | Captura con desplazamiento |
| `text` | Copiar texto de la pantalla |
| `video` | Grabar vídeo (otra vez: detener la grabación) |
| `gif` | Grabar GIF (otra vez: detener la grabación) |
| `markdown` | Ver y ordenar Markdown |
| `pause` | Pausar o reanudar la grabación en curso |

También vale cualquier otra acción que tenga atajo en tu versión de Stackshot.

- Da igual mayúsculas o minúsculas, y se puede escribir `/capture region`, `/capture:region`, `--capture=region` o
  `"--capture region"` (todo entre comillas, como hacen algunos lanzadores).
- Si Stackshot ya está abierto, la orden le llega al momento y la copia nueva se cierra sin mostrar nada.
- Si no lo está, Stackshot arranca como siempre (sin abrir su ventana) y hace la orden en cuanto está listo.
- Mientras estás eligiendo un área, las órdenes se ignoran, igual que los atajos. Varias pulsaciones seguidas no se
  acumulan: una orden que no se ha podido hacer en 30 segundos se descarta.
- Un modo desconocido no hace nada: queda anotado en el registro de Stackshot y el programa termina con el código 2.
  Códigos de salida: 0 = hecho o entregado, 1 = no se pudo entregar a la copia abierta, 2 = orden desconocida.
- Un archivo que exista con un nombre como `--open-home` se trata como archivo, no como orden.

**Logitech MX Keypad (Logi Options+):** asigna al botón la acción de abrir un programa con la ruta completa de
Stackshot y el modo como argumento. Stackshot se instala (también con el MSI) en `%LOCALAPPDATA%\Programs\Stackshot`:

```
"C:\Users\<tu usuario>\AppData\Local\Programs\Stackshot\Stackshot.exe" --capture region
```

## Directivas de empresa

Stackshot lee estas dos claves del registro; si las dos tienen el mismo valor, gana la del equipo (HKLM):

```
HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Stackshot    (todo el equipo; GPO o Intune)
HKEY_CURRENT_USER\SOFTWARE\Policies\Stackshot     (solo ese usuario)
```

| Valor | Tipo | Efecto |
|---|---|---|
| `SaveFolder` | REG_SZ o REG_EXPAND_SZ | Carpeta donde se guardan las capturas. Ruta completa (`C:\...` o `\\servidor\...`); admite variables como `%USERNAME%`. |
| `CheckUpdates` | DWORD `0` | No busca actualizaciones nunca (ni las instala). |
| `AutoUpdate` | DWORD `0` | No instala actualizaciones por su cuenta; avisa de cada versión nueva y se puede instalar a mano. |
| `Mascot` | DWORD `0` | Sin mascota: ni en la ventana, ni en el escritorio, ni sus saludos y consejos. |
| `SensitiveAuto` | DWORD `1` / `0` | `1`: tapa siempre los datos personales (DNI, IBAN, correos...) en cada captura. `0`: nunca. |
| `HistorySearch` | DWORD `0` | El Historial no lee el texto de las capturas ni guarda un índice de texto (borra el que hubiera): solo busca por el nombre. |
| `XmlWatchFolder` | REG_SZ o REG_EXPAND_SZ | Carpeta donde un programa deja cada mensaje SOAP como XML. Hace aparecer Ajustes > General > Avanzado > «Abrir los XML nuevos de una carpeta» con esa carpeta fija (el usuario solo decide si lo enciende). Sin esta directiva, el grupo solo existe donde ya estaba configurado. |

- En `CheckUpdates`, `AutoUpdate`, `Mascot` y `HistorySearch`, un `1` deja la decisión al usuario; en HKLM, aunque
  HKCU tenga un `0`.
- Un valor con otro tipo o fuera de rango se ignora y queda anotado en `%LOCALAPPDATA%\Stackshot\stackshot.log`; si
  estaba en HKLM, vale el de HKCU, si lo hay.
- Se leen al abrir Stackshot y cada vez que se abre una sección de su ventana: no hace falta reiniciar.
- En Ajustes, lo que fija la directiva aparece desactivado con el texto «Lo decide tu organización», y Acerca de
  avisa de que «Algunos ajustes los gestiona tu organización.».
- Los ajustes propios del usuario se conservan en `settings.ini`: si se quita la directiva, vuelve a tener los suyos.
- En una instalación con el paquete MSI, las actualizaciones ya las gestiona informática con cada nuevo `Stackshot.msi`.

Ejemplo (`stackshot-empresa.reg`):

```reg
Windows Registry Editor Version 5.00

[HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Stackshot]
"SaveFolder"="\\\\servidor\\capturas\\%USERNAME%"
"CheckUpdates"=dword:00000000
"Mascot"=dword:00000000
"SensitiveAuto"=dword:00000001
"HistorySearch"=dword:00000000
```

Para quitar todas las directivas:

```reg
Windows Registry Editor Version 5.00

[-HKEY_LOCAL_MACHINE\SOFTWARE\Policies\Stackshot]
```
