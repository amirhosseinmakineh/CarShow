using Carshow.Utilities.Hasher;
using CarShow.ApplicationService.Contract.Dtos.RoleDto;
using CarShow.ApplicationService.Contract.Dtos.User;
using CarShow.ApplicationService.Contract.IService;
using CarShow.Domain.IRepository;
using CarShow.Domain.Models;
using CarShow.Security.Token;
using Microsoft.EntityFrameworkCore;

namespace CarShow.ApplicationService.Services
{
    public class UserService : IUserService
    {
        private readonly IBaseRepository<Guid, User> _userRepository;
        private readonly IBaseRepository<long, UserRole> _userRoleRepository;
        private readonly IBaseRepository<long, Role> _roleRepository;
        private readonly ITokenGenerator _tokenGenerator;
        private readonly IBaseRepository<long, Order> orderRepository;
        private readonly IBaseRepository<long, OrderItem> orderItemRepository;
        private readonly IBaseRepository<long, Car> carRepository;
        private readonly IUnitOfWork _unitOfWork;
        private readonly IInstallmentCalculator _installmentCalculator;

        public UserService(
            IBaseRepository<Guid, User> userRepository,
            IBaseRepository<long, UserRole> userRoleRepository,
            IBaseRepository<long, Role> roleRepository,
            ITokenGenerator tokenGenerator,
            IBaseRepository<long, Order> orderRepository,
            IBaseRepository<long, OrderItem> orderItemRepository,
            IBaseRepository<long, Car> carRepository,
            IUnitOfWork unitOfWork,
            IInstallmentCalculator installmentCalculator)
        {
            _userRepository = userRepository;
            _userRoleRepository = userRoleRepository;
            _roleRepository = roleRepository;
            _tokenGenerator = tokenGenerator;
            this.orderRepository = orderRepository;
            this.orderItemRepository = orderItemRepository;
            this.carRepository = carRepository;
            _unitOfWork = unitOfWork;
            _installmentCalculator = installmentCalculator;
        }

        public async Task<Result<RegisterDto>> Register(RegisterDto dto)
        {
            var existingUser = _userRepository.GetAll()
                .FirstOrDefault(u => u.MobileNumber == dto.MobileNumber);

            if (existingUser != null)
                return Result<RegisterDto>
                    .Failure("کاربری با این شماره موبایل از قبل وجود دارد");

            var user = new User
            {
                Id = Guid.NewGuid(),
                MobileNumber = dto.MobileNumber,
                Password = PasswordHasher.HashPassword(dto.Password),
                IsDelete = false,
                Email = dto.Emaail,
                Name = dto.Name,
                Family = dto.Family,
            };

            try
            {
                await _unitOfWork.BeginTransactionAsync();

                await _userRepository.Create(user);
                await _userRepository.SaveChanges();

                await AddRoleToUser(user.Id, "Customer");

                await _unitOfWork.CommitAsync();
            }
            catch
            {
                await _unitOfWork.RollbackAsync();
                throw;
            }

            var outputDto = new RegisterDto
            {
                Id = user.Id,
                MobileNumber = user.MobileNumber,
                Password = null
            };

            return Result<RegisterDto>
                .Success(outputDto, "ثبت نام با موفقیت انجام شد");
        }



        public async Task AddRoleToUser(Guid userId, string roleName)
        {
            var role = _roleRepository.GetAll()
                .FirstOrDefault(r => r.RoleName == roleName);

            if (role == null)
                throw new Exception($"Role '{roleName}' یافت نشد");

            var existingUserRole = _userRoleRepository.GetAll()
                .FirstOrDefault(ur => ur.UserId == userId && ur.RoleId == role.Id);

            if (existingUserRole != null)
                return;

            var userRole = new UserRole
            {
                UserId = userId,
                RoleId = role.Id
            };

            await _userRoleRepository
                .Create(userRole);
            await _userRoleRepository
                .SaveChanges();
        }

        public async Task<Result<LoginDto>> Login(LoginDto dto)
        {
            var user = _userRepository.GetAll()
                .FirstOrDefault(u => u.MobileNumber == dto.MobileNumber);

            if (user == null || !PasswordHasher
                .VerifyPassword(dto.Password, user.Password))
                return Result<LoginDto>
                    .Failure("شماره موبایل یا رمز عبور اشتباه است");

            var roles = _userRoleRepository
                .GetAll()
                .Where(ur => ur.UserId == user.Id)
                .Join(_roleRepository.GetAll(),
                    ur => ur.RoleId,
                    r => r.Id,
                    (ur, r) => r)
                .ToList();

            var token = _tokenGenerator
                .GenerateToken(user, roles);
            dto.Token = token;

            return Result<LoginDto>.Success(dto, "ورود با موفقیت انجام شد");
        }

        public async Task<Result<object>> CreateUser(CreateUserDto dto)
        {
            var user = await _userRepository.GetAll()
                .FirstOrDefaultAsync(x => !x.IsDelete && x.MobileNumber == dto.MobileNumber);
            if (user == null)
            {
                if (string.IsNullOrWhiteSpace(dto.Password) || !await _roleRepository.GetAll().AnyAsync(r => r.RoleName == dto.RoleName))
                    return Result<object>.Failure("رمز عبور یا نقش معتبر نیست");
                user = new User()
                {
                    Id = Guid.NewGuid(),
                    Name = dto.Name,
                    Email = dto.Email,
                    Family = dto.Family,
                    Password = PasswordHasher.HashPassword(dto.Password),
                    MobileNumber = dto.MobileNumber,
                };
                await _unitOfWork.BeginTransactionAsync();
                try
                {
                    await _userRepository.Create(user);
                    await _userRepository.SaveChanges();
                    await AddRoleToUser(user.Id, dto.RoleName);
                    await _unitOfWork.CommitAsync();
                }
                catch
                {
                    await _unitOfWork.RollbackAsync();
                    throw;
                }
                return Result<object>.Success(new { user.Id }, "ثبت کاربر با موفقیت انجام شد");
            }
            else
                return Result<object>.Failure("ثبت کاربر با خطا مواجه شد");
        }

        public async Task<Result<object>> UpdateUser(UpdateUserDto dto)
        {
            var user = await _userRepository.GetById(dto.Id);
            if (user != null && !user.IsDelete)
            {
                if (await _userRepository.GetAll().AnyAsync(x => x.Id != dto.Id && !x.IsDelete && x.MobileNumber == dto.MobileNumber))
                    return Result<object>.Failure("شماره موبایل تکراری است");
                user.Name = dto.Name;
                user.Email = dto.Email;
                user.Family = dto.Family;
                if (!string.IsNullOrWhiteSpace(dto.Password))
                    user.Password = PasswordHasher.HashPassword(dto.Password);
                user.MobileNumber = dto.MobileNumber;
                if (!string.IsNullOrWhiteSpace(dto.RoleName) && !await _roleRepository.GetAll().AnyAsync(x => x.RoleName == dto.RoleName))
                    return Result<object>.Failure("نقش معتبر نیست");
                await _unitOfWork.BeginTransactionAsync();
                try
                {
                    await _userRepository.Update(user);
                    await _userRepository.SaveChanges();
                    if (!string.IsNullOrWhiteSpace(dto.RoleName))
                    {
                        var role = await _roleRepository.GetAll().FirstAsync(x => x.RoleName == dto.RoleName);
                        var existing = await _userRoleRepository.GetAll().Where(x => x.UserId == user.Id).ToListAsync();
                        foreach (var assignment in existing.Where(x => x.RoleId != role.Id))
                            await _userRoleRepository.Delete(assignment);
                        if (!existing.Any(x => x.RoleId == role.Id))
                            await _userRoleRepository.Create(new UserRole { UserId = user.Id, RoleId = role.Id });
                        await _userRoleRepository.SaveChanges();
                    }
                    await _unitOfWork.CommitAsync();
                }
                catch
                {
                    await _unitOfWork.RollbackAsync();
                    throw;
                }
                return Result<object>.Success(new { user.Id }, "ویرایش کاربر با موفقیت انجام شد");
            }
            else
                return Result<object>.Failure("ویرایش کاربر با خطا مواجه شد");

        }

        public async Task<Result<string>> DeleteUser(Guid id)
        {
            var user = await _userRepository.GetById(id);
            if (user != null && !user.IsDelete)
            {
                user.IsDelete = true;
                await _userRepository.Update(user);
                await _userRepository.SaveChanges();
                return Result<string>.Success("حذف کاربر با موفقیت انجام شد");
            }
            return Result<string>.Failure("حذف کاربر با خطا مواجه شد");
        }

        public object GetAllUsers(int pageSize = 15, int pageNumber = 0, string? search = null)
        {
            pageSize = Math.Clamp(pageSize, 1, 100);
            pageNumber = Math.Max(0, pageNumber);
            var query = _userRepository.GetAll().AsNoTracking().Where(x => !x.IsDelete);
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                query = query.Where(x => x.Name.Contains(term) || x.Family.Contains(term)
                    || (x.Email != null && x.Email.Contains(term)) || x.MobileNumber.Contains(term));
            }
            var totalCount = query.Count();
            var users = query.OrderBy(x => x.Id)
                .Skip((int)Math.Min((long)pageNumber * pageSize, int.MaxValue))
                .Take(pageSize)
                .Select(x => new UserDto
                {
                    Id = x.Id, Name = x.Name, Family = x.Family,
                    Email = x.Email, MobileNumber = x.MobileNumber,
                    RoleName = _userRoleRepository.GetAll()
                        .Where(ur => ur.UserId == x.Id)
                        .Join(_roleRepository.GetAll(), ur => ur.RoleId, r => r.Id, (ur, r) => r.RoleName)
                        .FirstOrDefault()
                }).ToList();
            return new { Items = users, TotalCount = totalCount, PageNumber = pageNumber, PageSize = pageSize };
        }

        public List<RoleDto> GetAllRoles()
        {
            return _roleRepository.GetAll().Select(x => new RoleDto()
            {
                RoleId = x.Id,
                RoleName = x.RoleName
            }).ToList();
        }

   

    }
}