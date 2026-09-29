import SwiftUI
import AVFoundation

/// Full-screen camera QR scanner view using AVCaptureSession to scan
/// terminal ASCII and graphic QR codes displayed by the PC server.
public struct QrScannerView: UIViewControllerRepresentable {
    public var onScanSuccess: (DiscoveredServer) -> Void
    public var onDismiss: () -> Void

    public init(onScanSuccess: @escaping (DiscoveredServer) -> Void, onDismiss: @escaping () -> Void) {
        self.onScanSuccess = onScanSuccess
        self.onDismiss = onDismiss
    }

    public func makeUIViewController(context: Context) -> ScannerViewController {
        let vc = ScannerViewController()
        vc.delegate = context.coordinator
        return vc
    }

    public func updateUIViewController(_ uiViewController: ScannerViewController, context: Context) {}

    public func makeCoordinator() -> Coordinator {
        Coordinator(onScanSuccess: onScanSuccess, onDismiss: onDismiss)
    }

    public class Coordinator: NSObject, ScannerViewControllerDelegate {
        let onScanSuccess: (DiscoveredServer) -> Void
        let onDismiss: () -> Void
        private var hasScanned = false

        init(onScanSuccess: @escaping (DiscoveredServer) -> Void, onDismiss: @escaping () -> Void) {
            self.onScanSuccess = onScanSuccess
            self.onDismiss = onDismiss
        }

        public func didScanCode(_ code: String) {
            guard !hasScanned else { return }
            hasScanned = true

            HapticEngine.shared.triggerTactileClick()

            if let server = QrScannerView.parseServer(from: code) {
                DispatchQueue.main.async {
                    self.onScanSuccess(server)
                }
            } else {
                // Allow rescanning if format was invalid
                DispatchQueue.main.asyncAfter(deadline: .now() + 1.5) {
                    self.hasScanned = false
                }
            }
        }

        public func didCancel() {
            onDismiss()
        }
    }

    /// Parses PC server QR string (omnipad://, http://, or ip:port).
    public static func parseServer(from raw: String) -> DiscoveredServer? {
        let clean = raw.trimmingCharacters(in: .whitespacesAndNewlines)
        guard !clean.isEmpty else { return nullIfEmpty(clean) }

        if let url = URL(string: clean), let scheme = url.scheme?.lowercased() {
            if scheme == "omnipad" {
                if let components = URLComponents(string: clean) {
                    if let ip = components.queryItems?.first(where: { $0.name == "ip" })?.value, !ip.isEmpty {
                        let port = components.queryItems?.first(where: { $0.name == "port" })?.value.flatMap(Int.init) ?? OmniPadProtocol.defaultWebPort
                        return DiscoveredServer(ip: ip, name: "PC Server", port: port)
                    }
                }

                if let host = url.host, !host.isEmpty {
                    let port = url.port ?? OmniPadProtocol.defaultWebPort
                    return DiscoveredServer(ip: host, name: "PC Server", port: port)
                }
            }

            if (scheme == "http" || scheme == "https"), let host = url.host, !host.isEmpty {
                let port = url.port ?? OmniPadProtocol.defaultWebPort
                return DiscoveredServer(ip: host, name: "PC Server", port: port)
            }
        }

        return nullIfEmpty(clean)
    }

    private static func nullIfEmpty(_ str: String) -> DiscoveredServer? {
        let trimmed = str.replacingOccurrences(of: "^[a-zA-Z]+://", with: "", options: .regularExpression)
            .trimmingCharacters(in: CharacterSet(charactersIn: "/"))

        let parts = trimmed.split(separator: ":")
        guard !parts.isEmpty else { return nil }

        let host = String(parts[0]).trimmingCharacters(in: .whitespaces)
        guard !host.isEmpty else { return nil }

        let port = parts.count > 1 ? Int(parts[1]) ?? OmniPadProtocol.defaultWebPort : OmniPadProtocol.defaultWebPort
        return DiscoveredServer(ip: host, name: "PC Server", port: port)
    }
}

public protocol ScannerViewControllerDelegate: AnyObject {
    func didScanCode(_ code: String)
    func didCancel()
}

public final class ScannerViewController: UIViewController, AVCaptureMetadataOutputObjectsDelegate {
    public weak var delegate: ScannerViewControllerDelegate?

    private var captureSession: AVCaptureSession?
    private var previewLayer: AVCaptureVideoPreviewLayer?
    private var torchButton: UIButton?
    private var isTorchOn = false

    public override func viewDidLoad() {
        super.viewDidLoad()
        view.backgroundColor = .black
        checkCameraPermissionAndSetup()
        setupOverlayUI()
    }

    public override func viewWillAppear(_ animated: Bool) {
        super.viewWillAppear(animated)
        if captureSession?.isRunning == false {
            DispatchQueue.global(qos: .userInitiated).async { [weak self] in
                self?.captureSession?.startRunning()
            }
        }
    }

    public override func viewWillDisappear(_ animated: Bool) {
        super.viewWillDisappear(animated)
        if captureSession?.isRunning == true {
            captureSession?.stopRunning()
        }
    }

    private func checkCameraPermissionAndSetup() {
        switch AVCaptureDevice.authorizationStatus(for: .video) {
        case .authorized:
            setupCaptureSession()
        case .notDetermined:
            AVCaptureDevice.requestAccess(for: .video) { [weak self] granted in
                DispatchQueue.main.async {
                    if granted { self?.setupCaptureSession() }
                }
            }
        default:
            showPermissionDeniedAlert()
        }
    }

    private func setupCaptureSession() {
        DispatchQueue.global(qos: .userInitiated).async { [weak self] in
            guard let self = self else { return }

            let session = AVCaptureSession()
            session.sessionPreset = .high

            guard let device = AVCaptureDevice.default(for: .video),
                  let input = try? AVCaptureDeviceInput(device: device),
                  session.canAddInput(input) else {
                return
            }

            session.addInput(input)

            let output = AVCaptureMetadataOutput()
            guard session.canAddOutput(output) else { return }
            session.addOutput(output)

            output.setMetadataObjectsDelegate(self, queue: DispatchQueue.main)
            output.metadataObjectTypes = [.qr]

            self.captureSession = session
            session.startRunning()

            DispatchQueue.main.async {
                let preview = AVCaptureVideoPreviewLayer(session: session)
                preview.videoGravity = .resizeAspectFill
                preview.frame = self.view.bounds
                self.view.layer.insertSublayer(preview, at: 0)
                self.previewLayer = preview
            }
        }
    }

    public func metadataOutput(_ output: AVCaptureMetadataOutput, didOutput metadataObjects: [AVMetadataObject], from connection: AVCaptureConnection) {
        guard let metadataObj = metadataObjects.first as? AVMetadataMachineReadableCodeObject,
              let rawString = metadataObj.stringValue else {
            return
        }

        delegate?.didScanCode(rawString)
    }

    public override func viewDidLayoutSubviews() {
        super.viewDidLayoutSubviews()
        previewLayer?.frame = view.bounds
    }

    private func setupOverlayUI() {
        // Close Button
        let closeBtn = UIButton(type: .system)
        let closeImg = UIImage(systemName: "xmark.circle.fill", withConfiguration: UIImage.SymbolConfiguration(pointSize: 28, weight: .bold))
        closeBtn.setImage(closeImg, for: .normal)
        closeBtn.tintColor = .white
        closeBtn.translatesAutoresizingMaskIntoConstraints = false
        closeBtn.addTarget(self, action: #selector(handleClose), for: .touchUpInside)
        view.addSubview(closeBtn)

        // Torch Toggle
        let torchBtn = UIButton(type: .system)
        let torchImg = UIImage(systemName: "flashlight.off.fill", withConfiguration: UIImage.SymbolConfiguration(pointSize: 24))
        torchBtn.setImage(torchImg, for: .normal)
        torchBtn.tintColor = .white
        torchBtn.translatesAutoresizingMaskIntoConstraints = false
        torchBtn.addTarget(self, action: #selector(toggleTorch), for: .touchUpInside)
        view.addSubview(torchBtn)
        self.torchButton = torchBtn

        // Prompt Label
        let promptLabel = UILabel()
        promptLabel.text = "Point camera at OmniPad Server QR code on PC screen"
        promptLabel.textColor = .white
        promptLabel.font = .systemFont(ofSize: 14, weight: .semibold)
        promptLabel.textAlignment = .center
        promptLabel.numberOfLines = 0
        promptLabel.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(promptLabel)

        // HUD View Finder Reticle
        let reticle = ReticleView()
        reticle.backgroundColor = .clear
        reticle.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(reticle)

        NSLayoutConstraint.activate([
            closeBtn.topAnchor.constraint(equalTo: view.safeAreaLayoutGuide.topAnchor, constant: 16),
            closeBtn.leadingAnchor.constraint(equalTo: view.safeAreaLayoutGuide.leadingAnchor, constant: 20),
            closeBtn.widthAnchor.constraint(equalToConstant: 44),
            closeBtn.heightAnchor.constraint(equalToConstant: 44),

            torchBtn.topAnchor.constraint(equalTo: view.safeAreaLayoutGuide.topAnchor, constant: 16),
            torchBtn.trailingAnchor.constraint(equalTo: view.safeAreaLayoutGuide.trailingAnchor, constant: -20),
            torchBtn.widthAnchor.constraint(equalToConstant: 44),
            torchBtn.heightAnchor.constraint(equalToConstant: 44),

            reticle.centerXAnchor.constraint(equalTo: view.centerXAnchor),
            reticle.centerYAnchor.constraint(equalTo: view.centerYAnchor),
            reticle.widthAnchor.constraint(equalToConstant: 240),
            reticle.heightAnchor.constraint(equalToConstant: 240),

            promptLabel.topAnchor.constraint(equalTo: reticle.bottomAnchor, constant: 24),
            promptLabel.leadingAnchor.constraint(equalTo: view.leadingAnchor, constant: 32),
            promptLabel.trailingAnchor.constraint(equalTo: view.trailingAnchor, constant: -32)
        ])
    }

    @objc private func handleClose() {
        delegate?.didCancel()
    }

    @objc private func toggleTorch() {
        guard let device = AVCaptureDevice.default(for: .video), device.hasTorch else { return }
        do {
            try device.lockForConfiguration()
            isTorchOn.toggle()
            device.torchMode = isTorchOn ? .on : .off
            device.unlockForConfiguration()

            let img = UIImage(systemName: isTorchOn ? "flashlight.on.fill" : "flashlight.off.fill")
            torchButton?.setImage(img, for: .normal)
            torchButton?.tintColor = isTorchOn ? .systemYellow : .white
        } catch {}
    }

    private func showPermissionDeniedAlert() {
        let alert = UIAlertController(
            title: "Camera Access Required",
            message: "OmniPad needs camera access to scan server QR codes. Please allow camera permissions in iOS Settings.",
            preferredStyle: .alert
        )
        alert.addAction(UIAlertAction(title: "Settings", style: .default) { _ in
            if let url = URL(string: UIApplication.openSettingsURLString) {
                UIApplication.shared.open(url)
            }
        })
        alert.addAction(UIAlertAction(title: "Cancel", style: .cancel) { [weak self] _ in
            self?.delegate?.didCancel()
        })
        present(alert, animated: true)
    }
}

/// Cyberpunk style targeting frame overlay
private final class ReticleView: UIView {
    override func draw(_ rect: CGRect) {
        guard let ctx = UIGraphicsGetCurrentContext() else { return }

        let cornerLength: CGFloat = 28.0
        let strokeColor = UIColor(red: 0.22, green: 0.74, blue: 0.97, alpha: 0.9).cgColor // #38bdf8

        ctx.setStrokeColor(strokeColor)
        ctx.setLineWidth(4.0)
        ctx.setLineCap(.round)

        let inset = rect.insetBy(dx: 2, dy: 2)

        // Top-Left
        ctx.move(to: CGPoint(x: inset.minX, y: inset.minY + cornerLength))
        ctx.addLine(to: CGPoint(x: inset.minX, y: inset.minY))
        ctx.addLine(to: CGPoint(x: inset.minX + cornerLength, y: inset.minY))

        // Top-Right
        ctx.move(to: CGPoint(x: inset.maxX - cornerLength, y: inset.minY))
        ctx.addLine(to: CGPoint(x: inset.maxX, y: inset.minY))
        ctx.addLine(to: CGPoint(x: inset.maxX, y: inset.minY + cornerLength))

        // Bottom-Right
        ctx.move(to: CGPoint(x: inset.maxX, y: inset.maxY - cornerLength))
        ctx.addLine(to: CGPoint(x: inset.maxX, y: inset.maxY))
        ctx.addLine(to: CGPoint(x: inset.maxX - cornerLength, y: inset.maxY))

        // Bottom-Left
        ctx.move(to: CGPoint(x: inset.minX + cornerLength, y: inset.maxY))
        ctx.addLine(to: CGPoint(x: inset.minX, y: inset.maxY))
        ctx.addLine(to: CGPoint(x: inset.minX, y: inset.maxY - cornerLength))

        ctx.strokePath()
    }
}
