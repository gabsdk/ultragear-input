# How the input switch works

Monitors accept commands over DDC/CI, an I2C channel that runs inside the video cable. The
command that changes the input is a "Set VCP Feature" frame sent to the monitor at I2C address
`0x37` (`0x6E` on the wire).

```
source  length  opcode  code  value-high  value-low  checksum
0x51    0x84    0x03    0x60  0x00        0x0F       0x...
```

The checksum is the XOR of `0x6E` and every byte before it.

## Two protocols

| Protocol | Source address | Code | Who uses it |
|---|---|---|---|
| Standard DDC/CI | `0x51` | `0x60` | Most monitors, and some LG models |
| LG | `0x50` | `0xF4` | Recent LG monitors, including most UltraGear models |

The input values also differ:

| Input | Standard | LG |
|---|---|---|
| DisplayPort 1 | 15 (`0x0F`) | 208 (`0xD0`) |
| DisplayPort 2 / USB-C | 16 (`0x10`) | 209 (`0xD1`) |
| USB-C / DisplayPort 3 | 27 (`0x1B`) | 210 (`0xD2`) |
| HDMI 1 | 17 (`0x11`) | 144 (`0x90`) |
| HDMI 2 | 18 (`0x12`) | 145 (`0x91`) |

Port names vary by model. If an entry switches to the wrong port, rename it in the settings file.

## Transports

| Platform | Standard | LG |
|---|---|---|
| macOS (Apple silicon) | `IOAVServiceWriteI2C` | `IOAVServiceWriteI2C` |
| Windows | `SetVCPFeature` from `dxva2.dll` | NVIDIA `NvAPI_I2CWrite`, then AMD `ADL_Display_DDCBlockAccess_Get` |

The Windows DDC/CI API always sends source address `0x51`, so the LG protocol needs the raw I2C
access offered by the NVIDIA and AMD drivers. Intel graphics on Windows only support the standard
protocol in this app.

## Shared test vectors

[`spec/protocol.json`](../spec/protocol.json) holds the default inputs, the expected bytes of
every frame, manufacturer ID decoding and settings validation cases. The Swift and C# test suites
both read this file, so the two apps cannot drift apart.
