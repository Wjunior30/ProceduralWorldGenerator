using System;

namespace ProceduralWorld.Core.Platforming
{
    // Independent of Unity physics: the editable block grid is the collision source.
    public interface IPlatformMap
    {
        int Columns { get; }
        int Rows { get; }
        float TileSize { get; }
        bool Solid(int x, int y);
    }

    [Serializable]
    public sealed class MovementSettings
    {
        public float width = .56f, height = 1.5f, crouchHeight = .85f;
        public float walkSpeed = 4.6f, runSpeed = 6.7f, crouchSpeed = 2f;
        public float groundAcceleration = 65f, airAcceleration = 38f, braking = 80f;
        public float jumpSpeed = 12.5f, gravity = 32f, fallGravity = 46f, terminalSpeed = 22f;
        public float coyoteTime = .1f, jumpBuffer = .12f;
        public float dashSpeed = 14f, dashDuration = .17f, dashCooldown = .48f;
        public float wallSlideSpeed = 2.8f, wallJumpSpeed = 7.5f, wallJumpLock = .16f;
        public bool doubleJump = true, wallJump = true;
    }

    public struct MovementInput
    {
        public float move;
        public bool run, crouch, jumpPressed, jumpHeld, dashPressed;
    }

    public sealed class PlatformMotor
    {
        const float Skin = .002f;
        readonly IPlatformMap map;
        readonly MovementSettings settings;
        float coyote, jumpQueued, dashLeft, dashWait, wallLock;
        int airJumps, dashDirection = 1;
        bool airDashUsed, groundDash;
        public float X { get; private set; }
        public float Y { get; private set; }
        public float VelocityX { get; private set; }
        public float VelocityY { get; private set; }
        public float Height { get; private set; }
        public float Width => settings.width;
        public int Facing { get; private set; } = 1;
        public bool Grounded { get; private set; }
        public bool Crouching => Height < settings.height - .01f;
        public bool Dashing => dashLeft > 0;
        public bool GroundDashing => Dashing && groundDash;
        public float DashProgress => 1f - Math.Min(1f, dashLeft / Math.Max(.001f, settings.dashDuration));
        public bool WallSliding { get; private set; }
        public int WallSide { get; private set; }
        public bool Jumped { get; private set; }
        public bool Landed { get; private set; }
        public float DashReady => 1f - Math.Min(1f, dashWait / Math.Max(.001f, settings.dashCooldown));

        public PlatformMotor(IPlatformMap map, MovementSettings settings)
        {
            this.map = map ?? throw new ArgumentNullException(nameof(map));
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Height = settings.height;
        }

        public bool Overlaps(float x, float y, float height)
        {
            float tile = map.TileSize;
            int left = (int)Math.Floor((x - Width / 2 + Skin) / tile);
            int right = (int)Math.Floor((x + Width / 2 - Skin) / tile);
            int bottom = (int)Math.Floor((y + Skin) / tile);
            int top = (int)Math.Floor((y + height - Skin) / tile);
            for (int cx = left; cx <= right; cx++)
            for (int cy = bottom; cy <= top; cy++)
                if (cx < 0 || cx >= map.Columns || cy < 0 || cy >= map.Rows || map.Solid(cx, cy)) return true;
            return false;
        }

        public bool IntersectsCell(int x, int y)
        {
            float s = map.TileSize;
            return X + Width / 2 > x * s && X - Width / 2 < (x + 1) * s && Y < (y + 1) * s && Y + Height > y * s;
        }

        public bool Place(float x, float y)
        {
            if (Overlaps(x, y, settings.height)) return false;
            X = x; Y = y; Height = settings.height; Facing = 1;
            Stop(); dashWait = 0; airDashUsed = false; airJumps = 0;
            Grounded = Overlaps(X, Y - .025f, Height);
            coyote = Grounded ? settings.coyoteTime : 0;
            return true;
        }

        public bool FindSpawn(float preferredX, float preferredY)
        {
            int center = (int)Math.Floor(preferredX / map.TileSize);
            int start = (int)Math.Floor(preferredY / map.TileSize);
            // Search near the original surface first, then other columns. Never carve blocks.
            for (int offset = 0; offset < map.Columns; offset++)
            {
                int cx = center + ((offset + 1) / 2) * (offset % 2 == 0 ? -1 : 1);
                if (cx < 1 || cx >= map.Columns - 1) continue;
                for (int dy = 0; dy < map.Rows * 2; dy++)
                {
                    int cy = start + ((dy + 1) / 2) * (dy % 2 == 0 ? -1 : 1);
                    if (cy < 1 || cy >= map.Rows - 3) continue;
                    float x = (cx + .5f) * map.TileSize, y = cy * map.TileSize;
                    if (map.Solid(cx, cy - 1) && !Overlaps(x, y, settings.height) && Place(x, y)) return true;
                }
            }
            return false;
        }

        public void Stop()
        {
            VelocityX = VelocityY = 0;
            dashLeft = jumpQueued = wallLock = 0;
            WallSliding = Jumped = Landed = false;
        }

        public void Tick(MovementInput input, float dt)
        {
            if (dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            // Bounded simulation interval; callers use a fixed timestep.
            dt = Math.Min(dt, .05f);
            Jumped = Landed = false;
            bool wasGrounded = Grounded;
            Grounded = VelocityY <= 0 && Overlaps(X, Y - .025f, Height);
            if (Grounded) { coyote = settings.coyoteTime; airJumps = 0; airDashUsed = false; }
            else coyote = Math.Max(0, coyote - dt);
            jumpQueued = input.jumpPressed ? settings.jumpBuffer : Math.Max(0, jumpQueued - dt);
            dashWait = Math.Max(0, dashWait - dt);
            wallLock = Math.Max(0, wallLock - dt);
            float move = Math.Max(-1, Math.Min(1, input.move));
            if (Math.Abs(move) > .01f && !Dashing && wallLock <= 0) Facing = move < 0 ? -1 : 1;

            bool wantCrouch = input.crouch || (Dashing && groundDash);
            if (wantCrouch) Height = settings.crouchHeight;
            else if (!Overlaps(X, Y, settings.height)) Height = settings.height;
            WallSide = Overlaps(X - .035f, Y + .04f, Math.Max(.1f, Height - .08f)) ? -1 :
                Overlaps(X + .035f, Y + .04f, Math.Max(.1f, Height - .08f)) ? 1 : 0;

            if (!Dashing && jumpQueued > 0)
            {
                if (coyote > 0) Jump(settings.jumpSpeed);
                else if (settings.wallJump && WallSide != 0 && !Grounded)
                {
                    Jump(settings.jumpSpeed); VelocityX = -WallSide * settings.wallJumpSpeed;
                    Facing = -WallSide; wallLock = settings.wallJumpLock;
                }
                else if (settings.doubleJump && airJumps < 1 && !Grounded)
                {
                    airJumps++; Jump(settings.jumpSpeed * .92f);
                }
            }

            if (input.dashPressed && !Dashing && dashWait <= 0 && (Grounded || !airDashUsed))
            {
                dashDirection = Math.Abs(move) > .01f ? (move < 0 ? -1 : 1) : Facing;
                Facing = dashDirection; groundDash = Grounded;
                if (groundDash) Height = settings.crouchHeight;
                else airDashUsed = true;
                dashLeft = settings.dashDuration; dashWait = settings.dashCooldown;
            }
            WallSliding = false;
            if (Dashing)
            {
                VelocityX = dashDirection * settings.dashSpeed; VelocityY = 0;
                dashLeft = Math.Max(0, dashLeft - dt);
            }
            else
            {
                float speed = Crouching ? settings.crouchSpeed : input.run ? settings.runSpeed : settings.walkSpeed;
                if (wallLock <= 0)
                    VelocityX = Towards(VelocityX, move * speed, (Grounded ? (Math.Abs(move) < .01f ? settings.braking : settings.groundAcceleration) : settings.airAcceleration) * dt);
                if (!input.jumpHeld && VelocityY > settings.jumpSpeed * .45f) VelocityY = settings.jumpSpeed * .45f;
                VelocityY = Math.Max(-settings.terminalSpeed, VelocityY - (VelocityY < 0 ? settings.fallGravity : settings.gravity) * dt);
                WallSliding = settings.wallJump && !Grounded && VelocityY < 0 && WallSide != 0 && move * WallSide > .1f;
                if (WallSliding) VelocityY = Math.Max(VelocityY, -settings.wallSlideSpeed);
            }

            bool allowStep = Grounded && !Jumped && VelocityY <= 0;
            float dx = VelocityX * dt, dyMove = VelocityY * dt;
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(dx), Math.Abs(dyMove)) / (map.TileSize * .2f)));
            bool blockedX = false, blockedY = false;
            Grounded = false;
            for (int i = 0; i < steps; i++)
            {
                if (!blockedX && !MoveAxis(dx / steps, true))
                {
                    bool stepped = false;
                    if (allowStep)
                    {
                        float oldY = Y;
                        float rise = map.TileSize + .005f;
                        float up = Sweep(rise, false);
                        if (up >= rise - Skin)
                        {
                            Y += up;
                            if (!Overlaps(X + dx / steps, Y, Height)) { X += dx / steps; stepped = true; }
                            else Y = oldY;
                        }
                    }
                    if (!stepped) { blockedX = true; VelocityX = 0; dashLeft = 0; }
                }
                if (!blockedY && !MoveAxis(dyMove / steps, false))
                {
                    blockedY = true; if (dyMove < 0) Grounded = true; VelocityY = 0;
                }
            }
            if (allowStep && !Grounded)
            {
                float drop = Sweep(-map.TileSize - .03f, false);
                if (drop > -map.TileSize - .029f) { Y += drop; Grounded = true; VelocityY = 0; }
            }
            if (!Grounded && VelocityY <= 0) Grounded = Overlaps(X, Y - .025f, Height);
            if (Grounded)
            {
                Landed = !wasGrounded; airJumps = 0; airDashUsed = false;
                if (jumpQueued > 0 && !Dashing) Jump(settings.jumpSpeed);
            }
        }

        void Jump(float speed)
        {
            VelocityY = speed; coyote = jumpQueued = 0; Grounded = false; Jumped = true;
        }
        static float Towards(float value, float target, float delta) => value < target ? Math.Min(target, value + delta) : Math.Max(target, value - delta);

        bool MoveAxis(float amount, bool horizontal)
        {
            if (Math.Abs(amount) < .000001f) return true;
            float moved = Sweep(amount, horizontal);
            if (horizontal) X += moved; else Y += moved;
            return Math.Abs(moved - amount) < .00001f;
        }
        float Sweep(float amount, bool horizontal)
        {
            // Check each small segment even for probes longer than one tile.
            int parts = Math.Max(1, (int)Math.Ceiling(Math.Abs(amount) / (map.TileSize * .2f)));
            float safe = 0;
            for (int i = 1; i <= parts; i++)
            {
                float next = amount * i / parts;
                if (!Overlaps(X + (horizontal ? next : 0), Y + (horizontal ? 0 : next), Height)) { safe = next; continue; }
                float blocked = next;
                for (int search = 0; search < 14; search++)
                {
                    float middle = (safe + blocked) * .5f;
                    if (Overlaps(X + (horizontal ? middle : 0), Y + (horizontal ? 0 : middle), Height)) blocked = middle;
                    else safe = middle;
                }
                return safe;
            }
            return amount;
        }
    }
}

