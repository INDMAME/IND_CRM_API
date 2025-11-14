Option Explicit

Dim shell, cmd

Set shell = CreateObject("WScript.Shell")

' Ruta al exe del API (AJUSTA si usas otra)
cmd = "C:\Windows\SysWOW64\cmd.exe /c " & _
      """C:\inetpub\wwwroot\IND_CRM_API\IND_CRM_API.exe"""

' 0 = oculto, False = no esperar a que termine
shell.Run cmd, 0, False
