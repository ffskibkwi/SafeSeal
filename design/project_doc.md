🛡️ SafeSeal Technical Specification v2.0 (Security-Hardened)1. Project VisionSafeSeal is a high-security Windows utility for storing sensitive identity documents. It uses hardware-linked, user-specific encryption to ensure that even if .seal files are exfiltrated by a remote attacker, they remain cryptographically useless outside the original user's session.2. Security Architecture2.1 Encryption Strategy (DPAPI + Entropy)To prevent cross-process decryption on the same machine, SafeSeal uses DataProtectionScope.CurrentUser combined with a unique application-level Entropy Salt.Scope: CurrentUser (Files can only be decrypted by the specific Windows user who encrypted them).Entropy: A static 16-byte array acts as a "secondary secret" to isolate SafeSeal data from other DPAPI-aware apps.2.2 Memory Hygiene (RAM Protection)To mitigate "Cold Boot" or memory-scraping attacks, decrypted buffers are pinned in memory and explicitly zeroed out after use.3. Storage Specification3.1 The .seal File FormatEvery vault item is stored with a 40-byte header to ensure integrity and future compatibility.OffsetLengthFieldDescription04MagicFixed ASCII: SEAL42VersionSchema version (e.g., 0x01 0x00)632HMACSHA-256 hash of plaintext for integrity check38nPayloadDPAPI-encrypted image data4. Implementation Details (C# / .NET 9)4.1 Hardened Vault ManagerC#using System.Security.Cryptography;
using System.Runtime.InteropServices;

public static class VaultManager {
    private static readonly byte[] _entropy = { 0x53, 0x61, 0x66, 0x65, 0x53, 0x65, 0x61, 0x6C, 0x56, 0x32, 0x53, 0x61, 0x6C, 0x74 };

    public static void Save(byte[] rawData, string path) {
        // 1. Calculate HMAC for integrity
        byte[] hash = SHA256.HashData(rawData);
        
        // 2. Encrypt with CurrentUser scope + Entropy
        byte[] encrypted = ProtectedData.Protect(rawData, _entropy, DataProtectionScope.CurrentUser);
        
        // 3. Write Header + Hash + Payload
        using var stream = File.Create(path);
        stream.Write(System.Text.Encoding.ASCII.GetBytes("SEAL")); // Magic
        stream.Write(new byte[] { 1, 0 }); // Version
        stream.Write(hash); // 32-byte HMAC
        stream.Write(encrypted);
    }

    public static byte[] LoadSecurely(string path) {
        byte[] fileData = File.ReadAllBytes(path);
        // [Logic to verify Magic and Version omitted for brevity]
        
        byte[] encryptedPart = fileData.Skip(38).ToArray();
        byte[] decrypted = ProtectedData.Unprotect(encryptedPart, _entropy, DataProtectionScope.CurrentUser);
        
        // Verification: Compare stored HMAC with decrypted data hash
        // Throw CryptographicException if mismatch
        return decrypted;
    }
}
4.2 Safe Memory DisposalC#public void ProcessAndClear(byte[] buffer) {
    try {
        // Use the buffer to render the watermark...
    }
    finally {
        // Explicitly zero the memory to prevent RAM leakage
        Array.Clear(buffer, 0, buffer.Length);
    }
}
5. UI & UX Requirements5.1 Watermark EngineTiling: Fixed 35° angle, opacity range [10% - 40%].Templates: * Standard: "ONLY FOR {Purpose} - {Date}"Restricted: "RESTRICTED USE BY {Recipient} ONLY"Auto-Macros: {Date} expands to current yyyy-MM-dd, {Machine} expands to NetBIOS name.5.2 Error HandlingDecryption Failure: Instead of a crash, display: "This file is locked to another user or device. Security policy prevents access."6. Updated Roadmap (Revised for Risk)Phase 1 (Core): Implement VaultManager with .seal header and HMAC verification. Perform cross-user testing (same machine).Phase 2 (UI): Build WPF Shell with ListView for vault management (Rename/Delete).Phase 3 (Optimization): Publish using ReadyToRun (R2R). Native AOT remains an experimental flag for tech-enthusiasts only.🛡️ Risk Register SummaryMitigated: Remote exfiltration (Safe via CurrentUser + Entropy).Mitigated: File tampering (Safe via HMAC verification).Residual Risk: A local attacker who has already compromised your active Windows session can still access the data via SafeSeal. (Acceptable per threat model).