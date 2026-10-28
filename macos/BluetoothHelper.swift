// Native CoreBluetooth bridge for the .NET desktop app. All BLE work runs on
// the main dispatch queue; stdin/stdout carry bounded JSON messages only.
import Foundation
import CoreBluetooth

let serviceID = CBUUID(string: "8b7e0001-5c8a-4c8e-9f24-367b495d7a11")
let rxID = CBUUID(string: "8b7e0002-5c8a-4c8e-9f24-367b495d7a11")
let txID = CBUUID(string: "8b7e0003-5c8a-4c8e-9f24-367b495d7a11")

func emit(_ event: String, _ value: String = "") {
    guard let data = try? JSONSerialization.data(withJSONObject: ["event": event, "value": value]),
          var line = String(data: data, encoding: .utf8) else { return }
    line += "\n"
    FileHandle.standardOutput.write(Data(line.utf8))
}

final class BluetoothBridge: NSObject, CBCentralManagerDelegate, CBPeripheralDelegate {
    private var central: CBCentralManager!
    private var discovered: [UUID: CBPeripheral] = [:]
    private var selected: CBPeripheral?
    private var rx: CBCharacteristic?
    private var tx: CBCharacteristic?
    private var writes: [Data] = []
    private var writing = false
    override init() {
        super.init()
        central = CBCentralManager(delegate: self, queue: .main)
    }
    func scan() {
        if central.state == .poweredOn && selected == nil {
            central.scanForPeripherals(withServices: [serviceID], options: [CBCentralManagerScanOptionAllowDuplicatesKey: true])
        }
    }
    func centralManagerDidUpdateState(_ central: CBCentralManager) {
        if central.state == .poweredOn { emit("state", "ready"); scan() }
        else {
            emit("state", central.state == .unauthorized ? "Bluetooth izni gerekiyor · Sistem Ayarları → Gizlilik ve Güvenlik" : "Bluetooth kapalı veya kullanılamıyor")
            if selected != nil { reset(); emit("disconnected") }
        }
    }
    func centralManager(_ central: CBCentralManager, didDiscover peripheral: CBPeripheral,
                        advertisementData: [String: Any], rssi RSSI: NSNumber) {
        discovered[peripheral.identifier] = peripheral
        emit("found", peripheral.identifier.uuidString)
    }
    func command(_ line: String) {
        guard line.utf8.count <= 1024,
              let data = line.data(using: .utf8),
              let object = (try? JSONSerialization.jsonObject(with: data)) as? [String: String],
              let action = object["action"] else { return }
        switch action {
        case "connect":
            guard central.state == .poweredOn, selected == nil,
                  let text = object["value"], let id = UUID(uuidString: text),
                  let peripheral = discovered[id] else { emit("error", "Bluetooth cihazı bulunamadı"); return }
            central.stopScan(); selected = peripheral; peripheral.delegate = self
            central.connect(peripheral, options: nil)
        case "disconnect":
            if let peripheral = selected { central.cancelPeripheralConnection(peripheral) }
            else { scan() }
        case "write":
            guard let peripheral = selected, peripheral.state == .connected, let characteristic = rx,
                  let text = object["value"], let payload = Data(base64Encoded: text),
                  !payload.isEmpty, payload.count <= 64, writes.isEmpty, !writing else {
                emit("error", "Bluetooth yazma isteği geçersiz"); return
            }
            let size = min(20, peripheral.maximumWriteValueLength(for: .withResponse))
            guard size > 0 else { emit("error", "Bluetooth paket boyutu geçersiz"); return }
            writes = stride(from: 0, to: payload.count, by: size).map { offset in
                payload.subdata(in: offset..<min(offset + size, payload.count))
            }
            writeNext(peripheral, characteristic)
        default: break
        }
    }
    private func writeNext(_ peripheral: CBPeripheral, _ characteristic: CBCharacteristic) {
        guard !writes.isEmpty else { writing = false; emit("written"); return }
        writing = true
        peripheral.writeValue(writes.removeFirst(), for: characteristic, type: .withResponse)
    }
    private func reset() {
        selected?.delegate = nil; selected = nil; rx = nil; tx = nil; writes.removeAll(); writing = false
    }
    private func fail(_ message: String) {
        emit("error", message)
        if let peripheral = selected { central.cancelPeripheralConnection(peripheral) }
    }
    func centralManager(_ central: CBCentralManager, didConnect peripheral: CBPeripheral) {
        guard peripheral == selected else { return }
        peripheral.discoverServices([serviceID])
    }
    func centralManager(_ central: CBCentralManager, didFailToConnect peripheral: CBPeripheral, error: Error?) {
        guard peripheral == selected else { return }
        reset(); emit("error", "Bluetooth bağlantısı kurulamadı"); scan()
    }
    func centralManager(_ central: CBCentralManager, didDisconnectPeripheral peripheral: CBPeripheral, error: Error?) {
        guard peripheral == selected else { return }
        reset(); emit("disconnected"); scan()
    }
    func peripheral(_ peripheral: CBPeripheral, didDiscoverServices error: Error?) {
        guard peripheral == selected else { return }
        guard error == nil, let service = peripheral.services?.first(where: { $0.uuid == serviceID })
        else { fail("Bluetooth hizmeti bulunamadı"); return }
        peripheral.discoverCharacteristics([rxID, txID], for: service)
    }
    func peripheral(_ peripheral: CBPeripheral, didDiscoverCharacteristicsFor service: CBService, error: Error?) {
        guard peripheral == selected else { return }
        guard error == nil,
              let input = service.characteristics?.first(where: { $0.uuid == rxID }), input.properties.contains(.write),
              let output = service.characteristics?.first(where: { $0.uuid == txID }), output.properties.contains(.notify)
        else { fail("Bluetooth protokolü uyuşmuyor"); return }
        rx = input; tx = output; peripheral.setNotifyValue(true, for: output)
    }
    func peripheral(_ peripheral: CBPeripheral, didUpdateNotificationStateFor characteristic: CBCharacteristic, error: Error?) {
        guard peripheral == selected, characteristic.uuid == txID else { return }
        if error == nil && characteristic.isNotifying { emit("ready") }
        else { fail("Bluetooth bildirimleri açılamadı") }
    }
    func peripheral(_ peripheral: CBPeripheral, didUpdateValueFor characteristic: CBCharacteristic, error: Error?) {
        guard peripheral == selected, characteristic.uuid == txID else { return }
        if error != nil { fail("Bluetooth okuma hatası"); return }
        if let data = characteristic.value { emit("data", data.base64EncodedString()) }
    }
    func peripheral(_ peripheral: CBPeripheral, didWriteValueFor characteristic: CBCharacteristic, error: Error?) {
        guard peripheral == selected, characteristic.uuid == rxID else { return }
        if error != nil { writes.removeAll(); writing = false; fail("Bluetooth yazma hatası"); return }
        writeNext(peripheral, characteristic)
    }
}

let bridge = BluetoothBridge()
DispatchQueue.global(qos: .utility).async {
    while let line = readLine() {
        DispatchQueue.main.async { bridge.command(line) }
    }
    // Parent exit (stdin EOF) must release BLE rather than leave an orphan.
    exit(0)
}
dispatchMain()
