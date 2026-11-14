# Guía rápida: preparar el aula para SERENA (H.U. 12.1)

Una página para el administrador de TI o el docente, antes de la clase.

## Parámetros de red

| Parámetro | Valor |
| --- | --- |
| Servidor | Computador del docente con Serena.Api (se abre con `Iniciar Serena.cmd`) |
| Puerto de las tablets | **5080**, HTTP. La API escucha en todas las interfaces (`0.0.0.0:5080`) |
| Puerto del docente | 5081, HTTPS, solo en el mismo computador (`localhost`) |
| Web docente | `http://localhost:5173`, solo en el mismo computador |
| Red | Tablets y servidor en la misma red Wi-Fi o LAN del aula |
| Router | Desactivar el aislamiento de clientes (*client isolation* o *AP isolation*) |
| Dirección en la tablet | `http://<IP del servidor>:5080`, por ejemplo `http://192.168.1.18:5080` |

## Antes de la clase (10 minutos)

1. **Conectar el servidor** a la red del aula y ver su IP: en `cmd`, `ipconfig`, y anotar la «Dirección IPv4» del adaptador Wi-Fi o Ethernet. Conviene reservar esa IP en el router (DHCP estático) para que no cambie entre clases.
2. **Abrir el puerto 5080** en el firewall de Windows del servidor (una sola vez, como administrador):
   `netsh advfirewall firewall add rule name="SERENA 5080" dir=in action=allow protocol=TCP localport=5080 profile=private`
   La red del aula debe estar marcada como **privada** en Windows.
3. **Iniciar SERENA** con `Iniciar Serena.cmd` y esperar a que abra la web docente.
4. **Probar desde una tablet:** abrir en el navegador `http://<IP>:5080/api/health`. Debe responder `{"status":"ok"}`. Si no responde, revisar la IP, el firewall y el aislamiento de clientes.
5. **Configurar cada tablet:** en la app, en la pantalla del código, tocar **Servidor**, escribir `http://<IP>:5080`, **Guardar** y **Cerrar**. La dirección queda guardada para las siguientes clases.
6. **Probar con varias tablets:** crear una sesión desde la web docente e ingresar el código en todas las tablets. Todas deben aparecer en la sala de espera; el indicador de conexión de cada tablet debe decir «Conectado».

## Si algo falla

| Síntoma en la tablet | Causa probable |
| --- | --- |
| «Conexión perdida · servidor no disponible» | La API está cerrada, la IP cambió o el firewall bloquea el puerto 5080 |
| `/api/health` no abre desde la tablet pero sí desde el servidor | Firewall de Windows o aislamiento de clientes en el router |
| «Sin conexión. Revisa la red.» al ingresar el código | La dirección guardada en **Servidor** no es la del servidor o no hay red |
| «Código inexistente o sesión vencida.» | La sesión expiró (4 horas) o se escribió otro código |
| La tablet se desconecta sola | Señal Wi-Fi débil: la API marca desconectada una tablet tras 15 s sin latido |

> Pendiente: confirmar en el aula real el modelo del router, si permite la comunicación entre clientes y cuántas tablets se conectan a la vez.
