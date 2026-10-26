"""Execute the researched game's actual ARM routines; never print private filenames."""
import hashlib
import struct
import sys
import zlib
from pathlib import Path

from unicorn import Uc, UC_ARCH_ARM, UC_MODE_ARM
from unicorn.arm_const import UC_ARM_REG_R0, UC_ARM_REG_R1, UC_ARM_REG_R2, UC_ARM_REG_SP, UC_ARM_REG_LR


def main():
    if len(sys.argv) < 2:
        raise SystemExit("Usage: verify-sd-list.py <code-decompressed.bin> [catalog.lst ...]")
    code = Path(sys.argv[1]).read_bytes()
    if hashlib.sha256(code).hexdigest() != "fa933da9573525ef3dc46e2f1b8437c75149077efff9c5c1ba5ecea57f4de2dd":
        raise SystemExit("Expected the researched Japanese v7.2.0 executable.")
    uc = Uc(UC_ARCH_ARM, UC_MODE_ARM)
    uc.mem_map(0x100000, 0x300000)
    uc.mem_write(0x100000, code)
    uc.mem_map(0x500000, 0x200000)

    def call(pc, a, b, c):
        uc.reg_write(UC_ARM_REG_SP, 0x6F0000)
        uc.reg_write(UC_ARM_REG_LR, 0x6FF000)
        for reg, value in ((UC_ARM_REG_R0, a), (UC_ARM_REG_R1, b), (UC_ARM_REG_R2, c)):
            uc.reg_write(reg, value)
        uc.emu_start(pc, 0x6FF000, count=5_000_000)
        return uc.reg_read(UC_ARM_REG_R0)

    plain = (("c" * 28 + ".kwz\n") + ("w" * 28 + ".kwz\n") + "\0\0").encode("utf-16le")
    key = code[0x274693:0x274693 + 61]
    crc = zlib.crc32(plain)
    synthetic = struct.pack("<HHH", crc >> 16, crc & 65535, 10) + bytes(v ^ key[i % 61] for i, v in enumerate(plain))
    samples = [synthetic] + [Path(p).read_bytes() for p in sys.argv[2:]]
    for index, data in enumerate(samples, 1):
        if not 6 <= len(data) <= 6806 or (len(data) - 6) % 68:
            raise ValueError(f"Invalid size in sample {index}")
        uc.mem_write(0x500000, data[6:])
        call(0x207AD0, 0x500000, 0x500000, len(data) - 6)
        decoded = bytes(uc.mem_read(0x500000, len(data) - 6))
        expected = (struct.unpack_from("<H", data)[0] << 16) | struct.unpack_from("<H", data, 2)[0]
        actual = call(0x222600, 0x500000, len(decoded), 0xFFFFFFFF)
        if actual != expected or actual != zlib.crc32(decoded):
            raise ValueError(f"Native CRC mismatch in sample {index}")
        call(0x207AD0, 0x500000, 0x500000, len(decoded))
        if bytes(uc.mem_read(0x500000, len(decoded))) != data[6:]:
            raise ValueError(f"Native XOR roundtrip mismatch in sample {index}")
        print(f"PASS native XOR/CRC/roundtrip: sample {index}, {(len(data) - 6) // 68} entries")

    guard = struct.unpack_from("<I", code, 0x107E14)[0]
    uc.mem_write(guard, struct.pack("<IIII", 1, 28, 1, 28))  # Initialize static guards; avoid unrelated runtime locks.
    for name, expected in (("c" * 28 + ".kwz", 1), ("c" * 27 + ".kwz", 0), ("x" * 28 + ".txt", 0)):
        uc.mem_write(0x500000, (name + "\0").encode("utf-16le"))
        if call(0x207D1C, 0x500000, 0, 0) != expected:
            raise ValueError("Native filename validation mismatch")
    print("PASS native filename validation")


if __name__ == "__main__":
    main()
