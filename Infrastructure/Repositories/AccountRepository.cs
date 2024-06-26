﻿using Application.DTOs.Request.Account;
using Application.DTOs.Response.Account;
using Application.DTOs.Response;
using Domain.Models.Authentication;
using Infrastructure.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Application.Extensions;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Reflection.Metadata;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Application.Interfaces;
using Mapster;
using Domain.Models;


namespace Infrastructure.Repositories
{
    public class AccountRepository(UserManager<ApplicationUser> userManager,
         RoleManager<IdentityRole> roleManager,
         SignInManager<ApplicationUser> signInManager,
         AppDbContext _context,
         IConfiguration config) : IAccountRepository
    {

        private async Task<ApplicationUser> FindUserByEmailAsync(string email)
            => await userManager.FindByEmailAsync(email);

        private async Task<IdentityRole> FindRoleByNameAsync(string roleName)
            => await roleManager.FindByNameAsync(roleName);

        private static string GenerateRefreshToken() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));

        private async Task<string> GenerateToken(ApplicationUser user)
        {
            try
            {
              
                var userClaims = new List<Claim>
                {
                new Claim(ClaimTypes.NameIdentifier, user.Id),
                new Claim(ClaimTypes.Name, user.Email!),
                new Claim(ClaimTypes.Email, user.Email!),
                new Claim(ClaimTypes.Role,(await userManager.GetRolesAsync(user)).FirstOrDefault()!.ToString()),
                new Claim("Fullname", user.Name!),
                new Claim("CartId", user.CartId!.ToString()),
                
            };

                var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config["Jwt:Key"]!));
                var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

                var token = new JwtSecurityToken(
                    issuer: config["Jwt:Issuer"],
                    audience: config["Jwt:Audience"],
                    claims: userClaims,
                    expires: DateTime.Now.AddMinutes(30),
                    signingCredentials: credentials
                    );
                return new JwtSecurityTokenHandler().WriteToken(token);
            }
            catch { return null!; }
        }

        private async Task<GeneralResponse> AssignUserToRole(ApplicationUser user, IdentityRole role)
        {
            if (user is null || role is null) return new GeneralResponse(false, "Model state cannot be done");
            if (await FindRoleByNameAsync(role.Name) == null)
                await CreateRoleAsync(role.Adapt(new CreateRoleDTO()));

            IdentityResult result = await userManager.AddToRoleAsync(user, role.Name);
            string error = CheckResponse(result);
            if (!string.IsNullOrEmpty(error))
                return new GeneralResponse(false, error);
            else
                return new GeneralResponse(true, $"{user.Name} assigned to {role.Name} role");
        }

        private static string CheckResponse(IdentityResult result)
        {
            if (!result.Succeeded)
            {
                var errors = result.Errors.Select(_ => _.Description);
                return string.Join(Environment.NewLine, errors);
            }
            return null!;

        }

        //MAIN IMPLEMENTAION :

        public async Task<GeneralResponse> ChangeUserRoleAsync(ChangeUserRoleDTO model)
        {
            if (await FindRoleByNameAsync(model.RoleName) is null) return new GeneralResponse(false, "Role not found");
            if (await FindUserByEmailAsync(model.UserEmail) is null) return new GeneralResponse(false, "User not found");

            var user = await FindUserByEmailAsync(model.UserEmail);
            var preRole = (await userManager.GetRolesAsync(user)).FirstOrDefault();
            var removeOldRole = await userManager.RemoveFromRoleAsync(user, preRole!);
            var error = CheckResponse(removeOldRole);
            if (!string.IsNullOrEmpty(error))
                return new GeneralResponse(false, error);

            var result = await userManager.AddToRoleAsync(user, model.RoleName);
            var respone = CheckResponse(result);
            if (!string.IsNullOrEmpty(error))
                return new GeneralResponse(false, respone);
            else
                return new GeneralResponse(true, $"Role Changed From {preRole} to {model.RoleName}");

        }

        public async Task<GeneralResponse> CreateAccountAsync(CreateAccountDTO model)
        {
            try
            {
                if (await FindUserByEmailAsync(model.Email) != null)
                    return new GeneralResponse(false, "Sorry user already exist");

                var user = new ApplicationUser()
                {
                    Name = model.Name,
                    Email = model.Email,
                    PasswordHash = model.Password,
                    UserName = model.Email                    
                };

                var cart = new Cart()
                {                    
                   Id = Guid.NewGuid().ToString(),
                   UserId = user.Id                   
                };


                user.CartId = cart.Id;
                await _context.Carts.AddAsync(cart);

                const string defaultRole = "User";                

                var result = await userManager.CreateAsync(user, model.Password);
                                
                string error = CheckResponse(result);
                if (!string.IsNullOrEmpty(error)) return new GeneralResponse(false, error);

                var response = await AssignUserToRole(user, new IdentityRole { Name = defaultRole });          

                string jwtToken = await GenerateToken(user);
                string refreshToken = GenerateRefreshToken();
                if (string.IsNullOrEmpty(jwtToken) || string.IsNullOrEmpty(refreshToken))
                {
                    return new GeneralResponse(false, "Error occured while logging in.");
                }
                else
                {
                    var saveResult = await SaveRefreshToken(user.Id, refreshToken);
                    if (saveResult.Flag)
                        return new GeneralResponse(true, $"{user.Name} successfully logged into the system", jwtToken, refreshToken);
                    else
                        return new GeneralResponse();

                }
            }

            catch (Exception ex)
            {
                return new GeneralResponse(false, ex.Message);
            }
        }

        public async Task CreateAdmin()
        {
            try
            {
                if ((await FindRoleByNameAsync(Constants.Role.Admin)) != null) return;
                var admin = new CreateAccountDTO()
                {
                    Name = "Admin",
                    Password = "Hamza@123",
                    Email = "admin@admin.com",
                    Role = Constants.Role.Admin,



                };
                await CreateAccountAsync(admin);
            }
            catch { }
        }

        public async Task<GeneralResponse> CreateRoleAsync(CreateRoleDTO model)
        {
            try
            {
                if ((await FindRoleByNameAsync(model.Name)) == null)
                {
                    var response = await roleManager.CreateAsync(new IdentityRole(model.Name));
                    var error = CheckResponse(response);
                    if (!string.IsNullOrEmpty(error)) throw new Exception(error);
                    else
                        return new GeneralResponse(true, $"{model.Name} created");
                }
                return new GeneralResponse(false, $"{model.Name} already created");
            }
            catch (Exception ex) { throw new Exception(ex.Message); }
        }

        public async Task<IEnumerable<GetRoleDTO>> GetRolesAsync() => (await roleManager.Roles.ToListAsync()).Adapt<IEnumerable<GetRoleDTO>>();


        public async Task<IEnumerable<GetUsersWithRolesResponseDTO>> GetUsersWithRolesAsync()
        {
            var allUsers = await userManager.Users.ToListAsync();
            if (allUsers is null) return null!;

            var List = new List<GetUsersWithRolesResponseDTO>();
            foreach (var user in allUsers)
            {
                var getUserRole = (await userManager.GetRolesAsync(user)).FirstOrDefault();
                var getRoleInfo = await roleManager.Roles.FirstOrDefaultAsync(r => r.Name!.ToLower() == getUserRole!.ToLower());
                List.Add(new GetUsersWithRolesResponseDTO()
                {
                    Name = user.Name,
                    Email = user.Email,
                    RoleId = getRoleInfo!.Id,
                    RoleName = getRoleInfo.Name
                });
            }
            return List;
        }

        public async Task<LoginResponse> LoginAccountAsync(LoginDTO model)
        {
            try
            {
                var user = await FindUserByEmailAsync(model.Email);
                if (user is null)
                    return new LoginResponse(false, "User not found");

                SignInResult result;
                try
                {
                    result = await signInManager.CheckPasswordSignInAsync(user, model.Password, false);
                }
                catch
                {
                    return new LoginResponse(false, "Invalid credentials");
                }
                if (!result.Succeeded)
                    return new LoginResponse(false, "Invalid credentials");

                string jwtToken = await GenerateToken(user);
                string refreshToken = GenerateRefreshToken();
                if (string.IsNullOrEmpty(jwtToken) || string.IsNullOrEmpty(refreshToken))
                {
                    return new LoginResponse(false, "Error occured while logging in.");
                }
                else
                {
                    var saveResult = await SaveRefreshToken(user.Id, refreshToken);
                    if (saveResult.Flag)
                        return new LoginResponse(true, $"{user.Name} successfully logged into the system", jwtToken, refreshToken);
                    else
                        return new LoginResponse();

                }

            }
            catch (Exception ex)
            {
                return new LoginResponse(false, ex.Message);
            }
        }

        public async Task<LoginResponse> RefreshTokenAsync(RefreshTokenDTO model)
        {
            var token = await _context.RefreshTokens.FirstOrDefaultAsync(t => t.Token == model.Token);
            if (token == null) return new LoginResponse();
            var user = await userManager.FindByIdAsync(token.UserId);
            string newToken = await GenerateToken(user);
            string newRefreshToken = GenerateRefreshToken();
            var saveResult = await SaveRefreshToken(user.Id, newRefreshToken);
            if (saveResult.Flag)
                return new LoginResponse(true, $"{user.Name} successfully re-logged in", newToken, newRefreshToken);
            else
                return new LoginResponse();
        }

        public async Task<GeneralResponse> SaveRefreshToken(string userId, string token)
        {
            try
            {
                var user = await _context.RefreshTokens.FirstOrDefaultAsync(t => t.UserId == userId);
                if (user == null)
                    _context.RefreshTokens.Add(new RefreshToken() { UserId = userId, Token = token });
                else
                    user.Token = token;

                await _context.SaveChangesAsync();
                return new GeneralResponse(true, null!);
            }
            catch (Exception ex)
            {
                return new GeneralResponse(false, ex.Message);
            }
        }

        public async Task<IEnumerable<GetUserDTO>> GetUser(string userId)
        {
            var user = await _context.Users.FindAsync(userId);
            if (user is null) return null!;

            var getUserDeatils = new GetUserDTO(user.Email, user.Name);
            return new List<GetUserDTO> { getUserDeatils };                
        }
    }
}
