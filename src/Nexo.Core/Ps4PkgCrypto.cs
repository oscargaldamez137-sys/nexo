using System;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;

namespace Nexo.Core
{
    // Public fake/development PKG key numbers, documented by LibOrbisPkg Util/Keys.cs.
    // These are format interoperability constants, not retail keys. Parser/crypto glue is Nexo's.
    internal static class Ps4PkgCrypto
    {
        private const string DerivedD =
            "32D903908FBDB08F572B285E0B8DB3EA5CD17EA890888CDD6A80BBB1DFC1F70D" +
            "AA32F0B77CCB88800E8B64B0BE4CD60E9B8C1E2A64E1F35CD77601415E935C94" +
            "FEDD4662C31B5AE2A0BC2DEBC3980AA7B7856970682B644AB31FCC7DDC7C26F4" +
            "77F65CF2AE5A442DD3AB16620419BAFB90FFE23050896ECB56B2EBC09116925E" +
            "308EAEC7945DFD35E120F8AD3EBC08BFC036749FD5BB5208FD0666F37AB304F4" +
            "75295DE95FAA1030B20F5A1AC12AB3FECB21AD80EC8F20091CDBC55894C29CC6" +
            "CE82653E5790BCA98B06B4F072F677DF9864F1ECFE372DBCAE8C08811FC3C989" +
            "1AC742824B2EDC8E8D73CEB1CC01D90870873C4408EC498F815AE240FF77FC0D";
        private const string DerivedN =
            "D212FC335F6DDB831609628B0356273782D477853529392D526B8C4C8CFB06C1" +
            "845BE7D4F7BCD24E6245CD2ABBD77776453655273FB3F5F98EDA4BEFAA59AEB3" +
            "9BEA5498D206326A58312AE0D44F90B50A7DECF43A9C52672D99318E0C43E682" +
            "FE0746E12E50D41F2D2F7ED908BA06B3BF2E203F4E3FFE44FFAA504357916994" +
            "49158282E40F4C8D9D2CC95B1D64BF888BD4C594E76547841EE57910FB989347" +
            "B97D8512A640982CF792BC951932EDE890560D65C1AA78C62E54FD5F54A1F67E" +
            "E5E05F61C120B4B9B4330870E4DF8956ED012946775F8CB8A9F51E2EB3B9BFE0" +
            "09B78D28D4A6C3B81E1F07EBB4120B95B88530FDDC3913D07CDC8FEDF9C9A3C1";
        private const string FakeD =
            "7F76CD0EE2D4DE051CC6D9A80E8DFA7BCA1EAA271A40F8F1228735DDDBFDEEF8" +
            "C2BCBD01FB8BE23E63B2B1225C56496E11BE07440B9A2666D1492C8FD31BCFA4" +
            "A1B8D1FBA49ED2212883098AF6A00BA3D60F9B6368CCBC0C4E145B27A4A9F42B" +
            "B9B87BC0E651AD1D77D46BB9CE20D126667E5E9EA2E96B90F373B8528F441103" +
            "0C1397393D132258D5438249DA6E7CA1C58CA5B009E0CE3DDFF49D3C9715E26A" +
            "C72B3C509323DBBA4A226644AC78BB0E1A2743B57167AFF4AB48469373D042AB" +
            "9363E56C9ADE5024C0237D99793F2207E0C148561BDF830912B42D456BC9C068" +
            "85999079961AD7F54D1F3783404AEC3937A680927DC580C7D66FFE8A7989C6B1";
        private const string FakeN =
            "C6CF71E7E59AF0D12A2C458BF92A0EC143058BC37117801DCD497DDE359D259B" +
            "A0D7A0F27D6C087EAA5502682B23C644B84418EB56CF16A24803C9E74F87EB3D" +
            "30C31588BF20E79DFF770CDE1D241E63A94F8ABF5BBE601968333BFCED9F474E" +
            "5FF8EACB3D00BD6701F92C6DC6AC1364E76714F3DC52696AB9832C4230131BB2" +
            "D8A5020D79ED96B10DF8CC0CDF81954F035809570E80692EFEFF5277EA7528A8" +
            "FBC9BEBF9FBBB7798E1805E180BD50349481D353C269A2D24CCF6CF4572C104A" +
            "3FFB22FD8B97E2C95BA62BCDD61B6BDB687F4BC2A05034C005E58DEF2467FF93" +
            "40CF2D62A2A050B1F13AA83DFD80D1F9B80522AFC8354590588EE33A7CBD3E27";

        internal static byte[] Sha(byte[] bytes) { using (var h = SHA256.Create()) return h.ComputeHash(bytes); }
        internal static byte[] Hmac(byte[] key, byte[] bytes) { using (var h = new HMACSHA256(key)) return h.ComputeHash(bytes); }
        internal static byte[] PfsKeys(byte[] ekpfs, byte[] seed, bool newCrypt)
        {
            if (ekpfs.Length != 32 || seed.Length != 16) throw new CryptographicException("Clave o semilla PFS no válida.");
            // Little-endian index 1; first half is tweak, second half is data.
            var key = newCrypt ? Hmac(ekpfs, seed) : ekpfs;
            return Hmac(key, new byte[] { 1, 0, 0, 0 }.Concat(seed).ToArray());
        }
        private static BigInteger Number(byte[] bigEndian) => new BigInteger(bigEndian.Reverse().Concat(new byte[1]).ToArray());
        private static byte[] Hex(string s) => Enumerable.Range(0, s.Length / 2).Select(i => Convert.ToByte(s.Substring(i * 2, 2), 16)).ToArray();
        internal static byte[] Unwrap(byte[] cipher, bool image)
        {
            if (cipher.Length != 256) throw new CryptographicException();
            var modulus = Number(Hex(image ? FakeN : DerivedN));
            var value = Number(cipher);
            if (value >= modulus) throw new CryptographicException();
            var raw = BigInteger.ModPow(value, Number(Hex(image ? FakeD : DerivedD)), modulus).ToByteArray();
            var decoded = new byte[256];
            for (int i = 0; i < Math.Min(raw.Length, 256); i++) decoded[255 - i] = raw[i];
            if (decoded[0] != 0 || decoded[1] != 2) throw new CryptographicException();
            int end = 2; while (end < 256 && decoded[end] != 0) end++;
            if (end < 10 || end != 223) throw new CryptographicException(); // exactly a 32-byte key
            return decoded.Skip(end + 1).ToArray();
        }
        internal static byte[] Cbc(byte[] data, byte[] ivKey)
        {
            using (var aes = Aes.Create())
            {
                aes.Mode = CipherMode.CBC; aes.Padding = PaddingMode.None;
                aes.Key = ivKey.Skip(16).Take(16).ToArray(); aes.IV = ivKey.Take(16).ToArray();
                using (var decrypt = aes.CreateDecryptor()) return decrypt.TransformFinalBlock(data, 0, data.Length);
            }
        }
    }
}
