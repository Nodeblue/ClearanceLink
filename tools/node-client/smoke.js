// Usage: node smoke.js [host] [port] [protocol: KittyHawk|SunRise]
// Connects to an XPConnect server with an independent SimConnect implementation, performs the handshake and
// sends a few typical ATC-client requests. Exits 0 when the handshake succeeds.
const { open, Protocol, SimConnectDataType, SimConnectPeriod, SimConnectConstants } = require('node-simconnect');

const [host = '127.0.0.1', port = '5000', protocolName = 'SunRise'] = process.argv.slice(2);

open('XPConnect smoke test', Protocol[protocolName], { remote: { host, port: Number(port) } })
  .then(({ recvOpen, handle }) => {
    console.log(`Connected to "${recvOpen.applicationName}" SimConnect ${recvOpen.simConnectVersionMajor}.${recvOpen.simConnectVersionMinor}`);
    handle.addToDataDefinition(0, 'PLANE ALTITUDE', 'feet', SimConnectDataType.FLOAT64);
    handle.addToDataDefinition(0, 'COM ACTIVE FREQUENCY:1', 'MHz', SimConnectDataType.FLOAT64);
    handle.requestDataOnSimObject(0, 0, SimConnectConstants.OBJECT_ID_USER, SimConnectPeriod.SECOND);
    handle.subscribeToSystemEvent(0, 'Pause');
    handle.mapClientEventToSimEvent(1, 'XPNDR_SET');
    setTimeout(() => { handle.close(); process.exit(0); }, 500);
  })
  .catch(err => { console.error('Connection failed:', err.message); process.exit(1); });
