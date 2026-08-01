-- ==========================================
-- 1. GEOGRAPHY (Minimal Seed for Dnipro)
-- ==========================================

DO $$ 
DECLARE 
    v_ukraine_id INT;
BEGIN
    INSERT INTO countries (name, code) 
    VALUES ('Ukraine', 'UKR')
    ON CONFLICT (code) DO NOTHING;

    SELECT id INTO v_ukraine_id FROM countries WHERE code = 'UKR';

    INSERT INTO regions (countryid, name) VALUES 
    (v_ukraine_id, 'Dnipropetrovsk Oblast')
    ON CONFLICT (countryid, name) DO NOTHING;

    INSERT INTO cities (id, regionid, name) VALUES 
    ('c0000000-0000-0000-0000-000000000005', (SELECT id FROM regions WHERE name = 'Dnipropetrovsk Oblast' AND countryid = v_ukraine_id), 'Dnipro')
    ON CONFLICT (regionid, name) DO NOTHING;

    RAISE NOTICE 'Minimal geography seeding completed successfully.';
END $$;

-- ==========================================
-- 2. SPORT DEFINITION
-- ==========================================

DO $$ 
DECLARE 
    sport_id uuid := '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f5f';
    config_id uuid := '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f70';
BEGIN
    -- Both sports and sportconfigurations are seeded in a single transaction block.
    -- Deferred FK check will validate defaultconfigid at the end of execution.
    INSERT INTO sports (id, name, shortname, defaultconfigid) 
    VALUES (sport_id, 'Water Polo', 'WP', config_id)
    ON CONFLICT (id) DO NOTHING;

    INSERT INTO playerpositiondefinitions (id, sportid, name, shortname) VALUES 
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f61', sport_id, 'Goalkeeper', 'GK'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f62', sport_id, 'Center Forward', 'CF'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f63', sport_id, 'Center Back', 'CB'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f64', sport_id, 'Driver', 'D'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f65', sport_id, 'Wing', 'W'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f66', sport_id, 'Utility', 'UTL')
    ON CONFLICT (id) DO NOTHING;

    INSERT INTO sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit, activeplayerslimit)
    VALUES (config_id, sport_id, true, 4, 8, '25x20m', 15, 13, 7)
    ON CONFLICT (id) DO NOTHING;

-- ==========================================
-- 3. TECHNICAL & TACTICAL ACTIONS
-- ==========================================

    INSERT INTO eventdefinitions (id, sportid, name, shortname, ispositive, createdat) VALUES 
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f81', sport_id, 'Goal', 'GOAL', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f82', sport_id, 'Assist', 'ASST', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f83', sport_id, 'Sprint Won', 'SPR+', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f84', sport_id, 'Exclusion Earned', 'EXCL+', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f85', sport_id, 'Penalty Earned', 'PEN+', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f86', sport_id, 'Steal', 'STL', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f87', sport_id, 'Shot Saved', 'SAVE', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f88', sport_id, 'Block', 'BLK', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f89', sport_id, 'Shot Missed', 'MISS', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f90', sport_id, 'Turnover', 'T-OVER', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f91', sport_id, 'Exclusion Received', 'EXCL-', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f92', sport_id, 'Penalty Committed', 'PEN-', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f93', sport_id, 'Sprint Lost', 'SPR-', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f94', sport_id, 'Critical Foul', 'C-FOUL', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f95', sport_id, 'Bad Goal Conceded', 'B-GOAL', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f96', sport_id, 'Tactical Error', 'T-ERR', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f97', sport_id, 'Defensive Transition Failure', 'D-TRANS', false, NOW())
    ON CONFLICT (id) DO NOTHING;
END $$;

-- ==========================================
-- 4. BASE CLUB (TTA Training Club)
-- ==========================================

INSERT INTO clubs (id, cityid, name, createdat) VALUES 
('11111111-1111-1111-1111-000000000001', 
    (SELECT c.id FROM cities c JOIN regions r ON c.regionid = r.id JOIN countries co ON r.countryid = co.id 
     WHERE c.name = 'Dnipro' AND r.name = 'Dnipropetrovsk Oblast' AND co.code = 'UKR' LIMIT 1), 'TTA Training Club', NOW())
ON CONFLICT (id) DO NOTHING;

-- ==========================================
-- 5. USERS (Without pre-assigned clubs/teams)
-- ==========================================

DO $$ 
DECLARE 
    v_user_id VARCHAR := 'auth0|69cf7ec5eff8f1358a0b9ae0'; 
    v_user_id_2 VARCHAR := 'auth0|698b956080889e5401cef7c5'; 
    v_user_id_3 VARCHAR := 'auth0|698b9bd69f764e2999518960';
BEGIN
    INSERT INTO public.users (id, email, displayname, createdat)
    VALUES (v_user_id, 'hlib.bondarev@gmail.com', 'Hlib Bondarev', NOW())
    ON CONFLICT (id) DO NOTHING;

    INSERT INTO public.users (id, email, displayname, createdat)
    VALUES (v_user_id_2, 'user1@example.com', 'Taras Shevchenko', NOW())
    ON CONFLICT (id) DO NOTHING;

    INSERT INTO public.users (id, email, displayname, createdat)
    VALUES (v_user_id_3, 'user2@example.com', 'Ivan Franko', NOW())
    ON CONFLICT (id) DO NOTHING;

    -- Assign Global FullControl (Admin) policy to v_user_id (Hlib Bondarev)
    INSERT INTO auth.accesspolicies (id, userid, role, targettype, targetid, createdat)
    SELECT '99999999-9999-9999-9999-999999999904', v_user_id, 0, 0, NULL, NOW()
    WHERE NOT EXISTS (
        SELECT 1 FROM auth.accesspolicies 
        WHERE userid = v_user_id AND targettype = 0
    );

END $$;

-- ==========================================
-- 6. PLAYERS (50 Static Club Players)
-- ==========================================

DO $$ 
DECLARE 
    v_tta_club_id uuid := '11111111-1111-1111-1111-000000000001';
BEGIN
    INSERT INTO public.players (id, homeclubid, firstname, lastname, birthdate, gender, createdat)
    SELECT 
        ('44444444-4444-4444-4444-' || LPAD(i::text, 12, '0'))::UUID AS id,
        v_tta_club_id AS homeclubid,
        'Player' AS firstname,
        i::text AS lastname,
        '2011-01-01'::DATE AS birthdate,
        0 AS gender,
        NOW() AS createdat
    FROM generate_series(1, 50) AS i
    ON CONFLICT (id) DO NOTHING;

    RAISE NOTICE 'Seed for 50 static players in TTA Training Club completed successfully.';
END $$;