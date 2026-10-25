using System.Security.Cryptography;

namespace LibKwz;

/// <summary>KWZ signatures cover the complete file body, excluding the last 256 bytes.</summary>
public static class KwzSigning
{
    /// <summary>Sign with the game-compatible key embedded in this assembly.</summary>
    public static byte[] Sign(ReadOnlySpan<byte> file)
    {
        using RSA key=CreateSigningKey();
        return Sign(file,key);
    }

    /// <summary>Verify with the game public key embedded in this assembly.</summary>
    public static bool Verify(ReadOnlySpan<byte> file)
    {
        using RSA key=CreateVerificationKey();
        return Verify(file,key);
    }

    public static byte[] Sign(ReadOnlySpan<byte> file, RSA privateKey)
    {
        if (file.Length < 256 || privateKey.KeySize != 2048) throw new ArgumentException("Expected a complete KWZ and RSA-2048 key.");
        byte[] result=file.ToArray();
        byte[] signature=privateKey.SignData(file[..^256],HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);
        signature.CopyTo(result,result.Length-256);
        return result;
    }
    public static bool Verify(ReadOnlySpan<byte> file, RSA publicKey) => file.Length>=256 &&
        publicKey.VerifyData(file[..^256],file[^256..],HashAlgorithmName.SHA256,RSASignaturePadding.Pkcs1);

    /// <summary>Create an owned RSA instance from the embedded DER resource. The caller must dispose it.</summary>
    public static RSA CreateSigningKey()
    {
        byte[] privateDer=ReadResource("KwzSigningKey.der",1192);
        RSA key=RSA.Create();
        try {
            key.ImportRSAPrivateKey(privateDer,out int count);
            using RSA pub=CreateVerificationKey();
            if(count!=privateDer.Length || key.KeySize!=2048 || !key.ExportRSAPublicKey().AsSpan().SequenceEqual(pub.ExportRSAPublicKey()))
                throw new InvalidDataException("The embedded KWZ key pair does not match.");
            return key;
        } catch { key.Dispose(); throw; }
        finally { CryptographicOperations.ZeroMemory(privateDer); }
    }

    /// <summary>Create an owned RSA instance containing only the embedded public key.</summary>
    public static RSA CreateVerificationKey()
    {
        byte[] publicDer=ReadResource("KwzVerificationKey.der",270);
        RSA key=RSA.Create();
        try {
            key.ImportRSAPublicKey(publicDer,out int count);
            if(count!=publicDer.Length || key.KeySize!=2048)
                throw new InvalidDataException("The embedded KWZ public key is invalid.");
            return key;
        } catch { key.Dispose(); throw; }
    }

    private static byte[] ReadResource(string name,int expectedLength)
    {
        using Stream resource=typeof(KwzSigning).Assembly.GetManifestResourceStream("LibKwz.Resources."+name)
            ?? throw new InvalidDataException("Missing embedded KWZ key resource: "+name);
        if(resource.Length!=expectedLength) throw new InvalidDataException("Invalid embedded KWZ key resource: "+name);
        byte[] data=new byte[expectedLength];
        resource.ReadExactly(data);
        return data;
    }
}
