# prism-launcher-account-protector
Both the source and compiled executable file have been provided.
Built with SmilerRyan's SimpleCSharpCompiler v1.1

Expected structure:
C:\Games\PrismLauncher\
C:\Games\PrismLauncher\prismlauncher.exe
C:\Games\PrismLauncher\UserData\accounts.json
C:\Games\PrismLauncher\UserData\output_xxxxxxxxxx.exe

Opening will encrypt all non-offline account types and save into the same file.
Afterwords, it will decrypt and then open prism launcher.
When it detects prism launcher closes, it will re-encrypt all non-offline accounts.

If opening prism launcher directly and NOT this encrypt/unlock tool, those accounts won't be shown in the UI.
For example, if you had 0 offline accounts and 2 non-offline accounts, the file accounts.json would look like this:

{
  "accounts": [
    {
      "type": "encrypted",
      "data": "..."
    },
    {
      "type": "encrypted",
      "data": "..."
    }
  ],
  "formatVersion": 3
}

Note, this is NOT a replacement for security, this is purely just to encrypt your account so they have to be decrypted before being used.

If you want actual security, i'd recommend creating a BitLocker drive, mounting it, and playing minecraft through that, then unmounting when done, but that's too overkill for this.
