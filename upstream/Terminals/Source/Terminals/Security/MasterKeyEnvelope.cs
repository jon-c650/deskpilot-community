using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
namespace Terminals.Security
{
    // Versioned authenticated key wrapping. Payload is the existing data key, not a
    // new password verifier; upgrading leaves every stored credential byte intact.
    internal static class MasterKeyEnvelope
    {
        internal const string Prefix="DP3$";
        private const int Iterations=600000;
        internal static bool IsModern(string value) {return value!=null && value.StartsWith(Prefix,StringComparison.Ordinal);}
        internal static string Wrap(string password,string key)
        {
            if(String.IsNullOrEmpty(password)) throw new ArgumentException("A master password is required.");
            byte[] salt=new byte[16],iv=new byte[16];using(var rng=RandomNumberGenerator.Create()) {rng.GetBytes(salt);rng.GetBytes(iv);}
            byte[] keys=Derive(password,salt);byte[] plaintext=Convert.FromBase64String(key);
            try {
                if(plaintext.Length!=32) throw new CryptographicException("Invalid stored data key.");
                byte[] ciphertext;
                using(var aes=Aes.Create()) {aes.Key=keys.Take(32).ToArray();aes.IV=iv;using(var enc=aes.CreateEncryptor()) ciphertext=enc.TransformFinalBlock(plaintext,0,plaintext.Length);}
                string header=Prefix+Iterations+"$"+Convert.ToBase64String(salt)+"$"+Convert.ToBase64String(iv)+"$"+Convert.ToBase64String(ciphertext);
                using(var mac=new HMACSHA256(keys.Skip(32).ToArray())) return header+"$"+Convert.ToBase64String(mac.ComputeHash(Encoding.UTF8.GetBytes(header)));
            } finally {Array.Clear(keys,0,keys.Length);Array.Clear(plaintext,0,plaintext.Length);}
        }
        internal static string Unwrap(string password,string record)
        {
            if(String.IsNullOrEmpty(password)) throw new CryptographicException("A master password is required.");
            string[] parts=record.Split('$');
            if(parts.Length!=6 || parts[0]!="DP3" || parts[1]!=Iterations.ToString()) throw new CryptographicException("Unsupported master-key envelope.");
            byte[] salt=Convert.FromBase64String(parts[2]),iv=Convert.FromBase64String(parts[3]),ciphertext=Convert.FromBase64String(parts[4]),tag=Convert.FromBase64String(parts[5]);
            if(salt.Length!=16 || iv.Length!=16 || ciphertext.Length!=48 || tag.Length!=32) throw new CryptographicException("Invalid master-key envelope.");
            byte[] keys=Derive(password,salt);
            try {
                byte[] calculated;using(var mac=new HMACSHA256(keys.Skip(32).ToArray())) calculated=mac.ComputeHash(Encoding.UTF8.GetBytes(String.Join("$",parts.Take(5).ToArray())));
                int difference=0;for(int i=0;i<tag.Length;i++) difference|=calculated[i]^tag[i];
                if(difference!=0) throw new CryptographicException("Master password or protected key is invalid.");
                using(var aes=Aes.Create()) {
                    aes.Key=keys.Take(32).ToArray();aes.IV=iv;
                    using(var dec=aes.CreateDecryptor()) {byte[] plaintext=dec.TransformFinalBlock(ciphertext,0,ciphertext.Length);try {return Convert.ToBase64String(plaintext);}finally {Array.Clear(plaintext,0,plaintext.Length);}}
                }
            } finally {Array.Clear(keys,0,keys.Length);}
        }
        private static byte[] Derive(string password,byte[] salt)
        {
            using(var kdf=new Rfc2898DeriveBytes(password,salt,Iterations,HashAlgorithmName.SHA256)) return kdf.GetBytes(64);
        }
    }
}
