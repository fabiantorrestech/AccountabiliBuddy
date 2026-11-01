package com.accountabilibuddy.shared.sync

import java.math.BigInteger
import java.security.AlgorithmParameters
import java.security.GeneralSecurityException
import java.security.KeyFactory
import java.security.KeyPair
import java.security.KeyPairGenerator
import java.security.PublicKey
import java.security.interfaces.ECPrivateKey
import java.security.interfaces.ECPublicKey
import java.security.spec.ECFieldFp
import java.security.spec.ECGenParameterSpec
import java.security.spec.ECParameterSpec
import java.security.spec.ECPoint
import java.security.spec.ECPublicKeySpec
import java.security.spec.PKCS8EncodedKeySpec
import javax.crypto.KeyAgreement

/**
 * A device's own ECDH P-256 key pair (ADR 54), as on the PC (Core's SyncDeviceKey): made once and kept; the public half
 * goes in the device's card ([SyncDeviceCard.publicKey]), the private half stays on the phone, sealed by an Android
 * Keystore key (see SyncIdentityStore). Other devices wrap the group's new link code for it with [RekeyWrap]. A public
 * key is the 65-byte uncompressed point: 0x04, X (32 bytes), Y (32 bytes). Plain JVM crypto, no Android classes.
 */
class SyncDeviceKey private constructor(private val pair: KeyPair) {
    /** This device's public key (65 bytes). */
    val publicKey: ByteArray = encodePoint((pair.public as ECPublicKey).w)

    /** The public key as standard base64, the form in the device card. */
    val publicKeyBase64: String get() = java.util.Base64.getEncoder().encodeToString(publicKey)

    /** The private key as PKCS#8 bytes, for the caller to seal before storing. */
    fun pkcs8(): ByteArray = pair.private.encoded

    /**
     * ECDH with [otherPublicKey]: the shared point's X coordinate (32 bytes), to be run through a key derivation before
     * use. Null when [otherPublicKey] isn't a valid P-256 point.
     */
    fun deriveSecret(otherPublicKey: ByteArray): ByteArray? {
        val other = decodePublicKey(otherPublicKey) ?: return null
        val agreement = KeyAgreement.getInstance("ECDH")
        agreement.init(pair.private)
        agreement.doPhase(other, true)
        return agreement.generateSecret()
    }

    companion object {
        /** The length of a public key: 0x04, X and Y. */
        const val PUBLIC_KEY_BYTES = 65

        private const val COORDINATE_BYTES = 32
        private const val UNCOMPRESSED_POINT = 0x04

        private val curve: ECParameterSpec by lazy {
            val parameters = AlgorithmParameters.getInstance("EC")
            parameters.init(ECGenParameterSpec("secp256r1"))
            parameters.getParameterSpec(ECParameterSpec::class.java)
        }

        fun generate(): SyncDeviceKey {
            val generator = KeyPairGenerator.getInstance("EC")
            generator.initialize(ECGenParameterSpec("secp256r1"))
            return SyncDeviceKey(generator.generateKeyPair())
        }

        /**
         * The key pair from [pkcs8] (see [SyncDeviceKey.pkcs8]) and [publicKey] (its [SyncDeviceKey.publicKey]), as the phone
         * kept them sealed; null when they aren't a P-256 private key and a point on the curve.
         */
        fun restore(pkcs8: ByteArray, publicKey: ByteArray): SyncDeviceKey? = try {
            val private = KeyFactory.getInstance("EC").generatePrivate(PKCS8EncodedKeySpec(pkcs8)) as ECPrivateKey
            val public = decodePublicKey(publicKey)
            if (public == null || private.params.order != curve.order) null else SyncDeviceKey(KeyPair(public, private))
        } catch (e: GeneralSecurityException) {
            null
        }

        /** Whether [publicKey] is 65 bytes that make a point on P-256. */
        fun isValidPublicKey(publicKey: ByteArray): Boolean = decodePublicKey(publicKey) != null

        internal fun decodePublicKey(bytes: ByteArray): PublicKey? {
            if (bytes.size != PUBLIC_KEY_BYTES || bytes[0].toInt() != UNCOMPRESSED_POINT) {
                return null
            }

            val x = BigInteger(1, bytes.copyOfRange(1, 1 + COORDINATE_BYTES))
            val y = BigInteger(1, bytes.copyOfRange(1 + COORDINATE_BYTES, PUBLIC_KEY_BYTES))
            if (!isOnCurve(x, y)) {
                return null
            }

            return try {
                KeyFactory.getInstance("EC").generatePublic(ECPublicKeySpec(ECPoint(x, y), curve))
            } catch (e: GeneralSecurityException) {
                null
            }
        }

        internal fun encodePoint(point: ECPoint): ByteArray {
            val encoded = ByteArray(PUBLIC_KEY_BYTES)
            encoded[0] = UNCOMPRESSED_POINT.toByte()
            fixed(point.affineX).copyInto(encoded, 1)
            fixed(point.affineY).copyInto(encoded, 1 + COORDINATE_BYTES)
            return encoded
        }

        // y^2 = x^3 + a*x + b (mod p), with 0 <= x, y < p: a point the curve really has.
        private fun isOnCurve(x: BigInteger, y: BigInteger): Boolean {
            val p = (curve.curve.field as ECFieldFp).p
            if (x >= p || y >= p) {
                return false
            }

            val left = y.multiply(y).mod(p)
            val right = x.pow(3).add(curve.curve.a.multiply(x)).add(curve.curve.b).mod(p)
            return left == right
        }

        // A coordinate as exactly 32 big-endian bytes (BigInteger adds a sign byte or drops leading zeros).
        private fun fixed(value: BigInteger): ByteArray {
            val raw = value.toByteArray()
            val out = ByteArray(COORDINATE_BYTES)
            val count = minOf(raw.size, COORDINATE_BYTES)
            raw.copyInto(out, COORDINATE_BYTES - count, raw.size - count, raw.size)
            return out
        }
    }
}
