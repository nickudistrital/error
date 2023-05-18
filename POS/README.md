# Documentation to understand the library

All steps in spanish here: [Manual](https://drive.google.com/file/d/1J_nEs3kbI740e2Okgl7xL--NJgC6FD8W/view?usp=sharing)

---

## For Visual Studio

---

## Change the DCL (dll)

Follow the next steps:

1. Request a new dll file pf DCL.
2. Open the solution
3. Open the POS solution
4. Open References
5. Right-click in DCL, open properties
6. Find the tag: PATH
    - Example: `C:\solven001\SOLVEN\bin\DCL\DCL.dll`
7. Open this folder in another window
8. Replace the file DCL.dll with the first step.

---

## General Configuration: Ethernet (cable)

In the solution POS; find and open the file: `pos_config.json`:

```JSON
 {
  "IpAddress": "IP from POS (S80, SP30)",
  "Port":  "PORT of POS (5000)",
  "Timeout":  "Miliseconds (15000)"
 }
```

Now this configuration is set in the file `PostService.cs` with the model `PosConfig.cs`

```cs
    private void InitializeDcl()
    {
        Trace("Initializing POS DCL...");

        //read DCL config from file
        var path = Path.Combine(Directory.GetCurrentDirectory(), "pos_config.json");
        var json = File.ReadAllText(path);
        var posConfig = JsonConvert.DeserializeObject<PosConfig>(json);
        ...
        ...
     }
```

---

## Integration DCL - Serail

The config for the machine; in this case, the description of all attributes is:

- StopBits: Default 1
- Baudrate: Default 115200
- ComPort: Communication Port; Default COM1 (depends on the SO port)
  - The case that the CPU has an available serial port, the port will be COM1.
  - Otherwise, you need to buy a USB to Serial converter that has the possible communication.
- Parity: None
- Timeout: 15000 (15 seconds)

```cs
    POS_TIMEOUT = 15000;
    
    ...
    ...

    private void InitializeDcl_RS232()
    {
        //todo delete if not used
        _dclRs232 = new DCL_RS232
        {
            StopBits = "1",
            Baudrate = "115200",
            // ComPort = "COM1", // Documentation
            // ComPort = "/dev/ttyUSB0",
            ComPort = "/dev/serial",
            DataBits = "8",
            Parity = "None",
            Timeout = POS_TIMEOUT
        };

        Trace(string.Format("Connected to: {0}", _dclRs232.ComPort));
    }
```

## To Buy

For make a purchase