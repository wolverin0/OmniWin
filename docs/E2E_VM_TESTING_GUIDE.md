# Guía Completa de Pruebas E2E y Validación Ground-Truth en Hyper-V (OmniWin)

Esta guía documenta la infraestructura, comandos y metodologías para ejecutar pruebas integrales de extremo a extremo (*End-to-End*) y validación de cambios reales en el sistema operativo (*Ground-Truth*) para **OmniWin** en entornos aislados de Hyper-V sobre Windows 10 y Windows 11.

---

## 1. Arquitectura del Laboratorio Hyper-V

El entorno consta de dos máquinas virtuales configuradas para emular instalaciones limpias del sistema operativo:

| Máquina Virtual | Sistema Operativo | Usuario | Contraseña | Modo de Conexión |
| :--- | :--- | :--- | :--- | :--- |
| `OmniWin-Lab-Win10` | Windows 10 Pro (22H2) | `gonzalo` | `gonzalo2026` | PowerShell Direct (`-VMId`) |
| `OmniWin-Lab-Win11` | Windows 11 Pro (23H2) | `gonzalo` | `gonzalo2026` | PowerShell Direct (`-VMId`) |

### ¿Por qué PowerShell Direct vía `VMId`?
1. **Aislamiento Total**: No depende de conmutadores virtuales externos, adaptadores NAT ni conectividad TCP/IP.
2. **Independencia de Nombre**: Evita colisiones de nombres o errores de resolución tipo *"The input VMName does not resolve to a single virtual machine"*.
3. **Privilegios de Hipervisor**: La conexión se realiza autenticando el usuario del guest sobre el VMBus de Hyper-V desde un proceso elevado en el host.

```powershell
$secPass = ConvertTo-SecureString "gonzalo2026" -AsPlainText -Force
$cred = New-Object System.Management.Automation.PSCredential("gonzalo", $secPass)
$vm = Get-VM -Name "OmniWin-Lab-Win10" | Select-Object -First 1
$s = New-PSSession -VMId $vm.Id -Credential $cred -ErrorAction Stop
```

---

## 2. Pipeline de Compilación y Distribución

Las máquinas virtuales limpias de Windows no incluyen el runtime de escritorio de .NET 9.0. Por ello, OmniWin **siempre** debe compilarse y distribuirse como aplicación autónoma (*self-contained*):

### Compilación y Empaquetado:
```powershell
# 1. Compilar binarios con CoreCLR embebido
dotnet publish OmniWin.UI/OmniWin.UI.csproj -c Release -r win-x64 --self-contained true -o publish/self-contained

# 2. Empaquetar en archivo ZIP (comprime 289 archivos a ~61 MB para transferencia instantánea)
Compress-Archive -Path publish/self-contained/* -DestinationPath publish/omniwin-sc.zip -Force
```

### Despliegue en la VM Invitada:
1. Copiar `omniwin-sc.zip` hacia `C:\Users\gonzalo\omniwin-sc.zip` en la sesión remota.
2. Descomprimir en `C:\OmniWin`.
3. Desbloquear archivos con `Unblock-File` y otorgar permisos completos con `icacls "C:\OmniWin" /grant "Everyone:(OI)(CI)F" /T /Q`.

---

## 3. Conducción Automatizada de la Interfaz (UI Automation)

Para interactuar con la aplicación dentro del escritorio gráfico de la VM sin requerir interacción manual del usuario:

### 1. Lanzamiento en Sesión Interactiva con Máximos Privilegios
Se utiliza el Programador de Tareas (`schtasks`) con las banderas `/it` (interactivo) y `/rl highest` (token de administrador elevado):
```powershell
schtasks /create /tn RunOmniSC /tr "powershell.exe -NoProfile -WindowStyle Hidden -ExecutionPolicy Bypass -File C:\Users\gonzalo\run_sc.ps1" /sc once /st 23:59 /f /ru gonzalo /rp gonzalo2026 /it /rl highest
schtasks /run /tn RunOmniSC
```

### 2. Conducción con UI Automation e `InvokePattern`
En lugar de depender de clics simulados con coordenadas de cursor (frágiles ante cambios de resolución o escala DPI), los botones clave implementan `AutomationProperties.AutomationId`:
* `BtnNext`: Avanza entre los pasos 1 a 6.
* `ChkCreateRestorePoint`: Casilla de verificación del punto de restauración.
* `BtnFinish`: Botón final *"🚀 Aplicar y Finalizar"*.

```powershell
$cond = New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::AutomationIdProperty, "BtnFinish")
$el = $omniWin.FindFirst([System.Windows.Automation.TreeScope]::Descendants, $cond)
if ($el) {
    $inv = $el.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern) -as [System.Windows.Automation.InvokePattern]
    $inv.Invoke()
}
```

---

## 4. Matriz de Validación Ground-Truth (Comprobación en el Registro de Windows)

Un test E2E exitoso no sólo verifica que el botón se presionó, sino que audita directamente en el registro y servicios del sistema operativo que las claves cambiaron de su valor predeterminado al valor optimizado:

| Optimización | Ruta en el Registro | Tipo | Valor Predeterminado | Valor Optimizado |
| :--- | :--- | :--- | :--- | :--- |
| **Startup Delay** | `HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Serialize` | `DWORD` | Inexistente / >0 | `0` |
| **Menu Show Delay** | `HKCU:\Control Panel\Desktop` | `String` | `"400"` | `"10"` |
| **Max Connections** | `HKCU:\Software\Microsoft\Windows\CurrentVersion\Internet Settings` | `DWORD` | `2` o `6` | `16` |
| **NTFS Memory Usage** | `HKLM:\SYSTEM\CurrentControlSet\Control\FileSystem` | `DWORD` | `0` (Default) | `2` (High Cache) |
| **AutoChk Timeout** | `HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager` | `DWORD` | `8` o `10` | `2` |
| **DNS Cache TTL** | `HKLM:\SYSTEM\CurrentControlSet\Services\Dnscache\Parameters` | `DWORD` | Inexistente | `86400` |

---

## 5. Comandos de Ejecución

### Ejecución Maestra (Ambas VMs en 1 Clic):
Para compilar, publicar, desplegar y validar en Windows 10 y Windows 11 de forma totalmente desatendida:
```powershell
powershell.exe -ExecutionPolicy Bypass -File .\scripts\test-e2e-both-vms.ps1
```

### Ejecutar sólo en Windows 10:
```powershell
powershell.exe -ExecutionPolicy Bypass -File .\scripts\run-deploy-and-drive.ps1
```

### Ejecutar sólo en Windows 11:
```powershell
powershell.exe -ExecutionPolicy Bypass -File .\scripts\run-deploy-win11.ps1
```

---

## 6. Reportes y Evidencia Gráfica

Todos los reportes y capturas se guardan automáticamente en:
`C:\Users\pauol\Source\Repos\OmniWin\scripts\reports\`

* `OmniWin-Step-1.png` a `OmniWin-Step-7.png`: Pantallas de cada uno de los 7 pasos del asistente.
* `OmniWin-PostWizard-Dashboard.png`: Dashboard principal de OmniWin revelado tras aplicar las optimizaciones y purgar la memoria RAM.
* `OmniWin-Win10-Cycle.log` / `OmniWin-Win11-Cycle.log`: Trazas completas de ejecución y validación de cada máquina.
