using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using MfaApiDemo.Models;
using QRCoder;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System;

namespace MfaApiDemo.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly SignInManager<ApplicationUser> _signInManager;

        public AuthController(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager)
        {
            _userManager = userManager;
            _signInManager = signInManager;
        }


        [HttpGet("qr-code")]
        public async Task<IActionResult> GetQrCode([FromQuery] string email)
        {
            var user = await _userManager.FindByEmailAsync(email);
            if (user == null) return NotFound("User not found");

            var key = await _userManager.GetAuthenticatorKeyAsync(user);
            if (string.IsNullOrEmpty(key))
            {
                await _userManager.ResetAuthenticatorKeyAsync(user);
                key = await _userManager.GetAuthenticatorKeyAsync(user);
            }

            var issuer = "MyApp";
            var totpUri = $"otpauth://totp/{issuer}:{email}?secret={key}&issuer={issuer}";
            var encodedUri = Uri.EscapeDataString(totpUri);

            using var qrGenerator = new QRCodeGenerator();
            using var qrCodeData = qrGenerator.CreateQrCode(totpUri, QRCodeGenerator.ECCLevel.Q);
            using var qrCode = new QRCode(qrCodeData);
            using var bitmap = qrCode.GetGraphic(20);
            using var ms = new MemoryStream();

            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png); // <-- fully qualified name
            var qrBytes = ms.ToArray();
            var base64 = Convert.ToBase64String(qrBytes);

            return Ok(new
            {
                ManualEntryKey = key,
                QrCodeImageBase64 = $"data:image/png;base64,{base64}"
            });
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register(RegisterDto dto)
        {
            var user = new ApplicationUser { UserName = dto.Email, Email = dto.Email };
            var result = await _userManager.CreateAsync(user, dto.Password);

            if (!result.Succeeded)
                return BadRequest(result.Errors);

            await _userManager.ResetAuthenticatorKeyAsync(user);
            var key = await _userManager.GetAuthenticatorKeyAsync(user);

            return Ok(new
            {
                ManualEntryKey = key,
                AuthenticatorUri = $"otpauth://totp/MyApp:{dto.Email}?secret={key}&issuer=MyApp"
            });
        }
        [HttpPost("verify-mfa")]
        public async Task<IActionResult> VerifyMfa(VerifyMfaDto dto)
        {
            var user = await _userManager.FindByEmailAsync(dto.Email);
            if (user == null)
            {
                return Unauthorized("User not found.");
            }

            var isValid = await _userManager.VerifyTwoFactorTokenAsync(
                user, TokenOptions.DefaultAuthenticatorProvider, dto.Code);

            if (!isValid)
            {
                // Additional custom error handling if token is expired or invalid
                return Unauthorized("Invalid or expired verification code.");
            }

            return Ok("MFA verification successful!");
        }
    }

    public class RegisterDto
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }

    public class VerifyMfaDto
    {
        public string Email { get; set; }
        public string Code { get; set; }
    }
}
