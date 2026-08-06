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
-- 2. SPORT DEFINITIONS & CONFIGURATIONS
-- ==========================================

DO $$ 
DECLARE 
    -- Water Polo IDs
    wp_sport_id uuid := '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f5f';
    wp_config_id uuid := '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f70';
    wp_config_alt_id uuid := '6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f71';

    -- Football IDs
    fb_sport_id uuid := '7a3f9c2b-8c4d-5e6f-9a0b-1c2d3e4f5a6b';
    fb_config_id uuid := '7a3f9c2b-8c4d-5e6f-9a0b-1c2d3e4f5a70';

    -- Basketball IDs
    bb_sport_id uuid := '8b4c0e3c-9d5e-6f7a-0b1c-2d3e4f5a6b7c';
    bb_config_id uuid := '8b4c0e3c-9d5e-6f7a-0b1c-2d3e4f5a6b80';
BEGIN
    -- -------------------------------------------------------------------------
    -- A. WATER POLO SEEDING
    -- -------------------------------------------------------------------------
    INSERT INTO sports (id, name, shortname, defaultconfigid) 
    VALUES (wp_sport_id, 'Water Polo', 'WP', wp_config_id)
    ON CONFLICT DO NOTHING;

    INSERT INTO playerpositiondefinitions (id, sportid, name, shortname) VALUES 
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f61', wp_sport_id, 'Goalkeeper', 'GK'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f62', wp_sport_id, 'Center Forward', 'CF'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f63', wp_sport_id, 'Center Back', 'CB'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f64', wp_sport_id, 'Driver', 'D'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f65', wp_sport_id, 'Wing', 'W'),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f66', wp_sport_id, 'Utility', 'UTL')
    ON CONFLICT (id) DO NOTHING;

    -- Standard Water Polo Configuration
    INSERT INTO sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit, activeplayerslimit)
    VALUES (wp_config_id, wp_sport_id, true, 4, 8, '30x20m', 15, 13, 7)
    ON CONFLICT (id) DO NOTHING;

    -- Alternative Water Polo Configuration (Smaller field 25x15m, 6 active players)
    INSERT INTO sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit, activeplayerslimit)
    VALUES (wp_config_alt_id, wp_sport_id, true, 4, 7, '25x15m', 13, 11, 6)
    ON CONFLICT (id) DO NOTHING;

    -- Water Polo Event Definitions
    INSERT INTO eventdefinitions (id, sportid, name, shortname, ispositive, createdat) VALUES 
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f81', wp_sport_id, 'Goal', 'GOAL', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f82', wp_sport_id, 'Assist', 'ASST', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f83', wp_sport_id, 'Sprint Won', 'SPR+', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f84', wp_sport_id, 'Exclusion Earned', 'EXCL+', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f85', wp_sport_id, 'Penalty Earned', 'PEN+', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f86', wp_sport_id, 'Steal', 'STL', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f87', wp_sport_id, 'Shot Saved', 'SAVE', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f88', wp_sport_id, 'Block', 'BLK', true, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f89', wp_sport_id, 'Shot Missed', 'MISS', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f90', wp_sport_id, 'Turnover', 'T-OVER', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f91', wp_sport_id, 'Exclusion Received', 'EXCL-', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f92', wp_sport_id, 'Penalty Committed', 'PEN-', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f93', wp_sport_id, 'Sprint Lost', 'SPR-', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f94', wp_sport_id, 'Critical Foul', 'C-FOUL', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f95', wp_sport_id, 'Bad Goal Conceded', 'B-GOAL', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f96', wp_sport_id, 'Tactical Error', 'T-ERR', false, NOW()),
    ('6f2e8f1a-7b3c-4d5e-8f9a-0b1c2d3e4f97', wp_sport_id, 'Defensive Transition Failure', 'D-TRANS', false, NOW())
    ON CONFLICT (id) DO NOTHING;

    -- -------------------------------------------------------------------------
    -- B. FOOTBALL SEEDING
    -- -------------------------------------------------------------------------
    INSERT INTO sports (id, name, shortname, defaultconfigid) 
    VALUES (fb_sport_id, 'Football', 'FB', fb_config_id)
    ON CONFLICT DO NOTHING;

    INSERT INTO playerpositiondefinitions (id, sportid, name, shortname) VALUES 
    ('7a3f9c2b-8c4d-5e6f-9a0b-1c2d3e4f5a61', fb_sport_id, 'Goalkeeper', 'GK'),
    ('7a3f9c2b-8c4d-5e6f-9a0b-1c2d3e4f5a62', fb_sport_id, 'Defender', 'DEF'),
    ('7a3f9c2b-8c4d-5e6f-9a0b-1c2d3e4f5a63', fb_sport_id, 'Midfielder', 'MID'),
    ('7a3f9c2b-8c4d-5e6f-9a0b-1c2d3e4f5a64', fb_sport_id, 'Forward', 'FWD')
    ON CONFLICT (id) DO NOTHING;

    INSERT INTO sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit, activeplayerslimit)
    VALUES (fb_config_id, fb_sport_id, false, 2, 45, '105x68m', 23, 18, 11)
    ON CONFLICT (id) DO NOTHING;

    INSERT INTO eventdefinitions (id, sportid, name, shortname, ispositive, createdat) VALUES 
    ('7a3f9c2b-8c4d-5e6f-9a0b-1c2d3e4f5a81', fb_sport_id, 'Goal', 'GOAL', true, NOW()),
    ('7a3f9c2b-8c4d-5e6f-9a0b-1c2d3e4f5a82', fb_sport_id, 'Assist', 'ASST', true, NOW()),
    ('7a3f9c2b-8c4d-5e6f-9a0b-1c2d3e4f5a83', fb_sport_id, 'Key Pass', 'KP', true, NOW()),
    ('7a3f9c2b-8c4d-5e6f-9a0b-1c2d3e4f5a84', fb_sport_id, 'Yellow Card', 'YC', false, NOW()),
    ('7a3f9c2b-8c4d-5e6f-9a0b-1c2d3e4f5a85', fb_sport_id, 'Red Card', 'RC', false, NOW()),
    ('7a3f9c2b-8c4d-5e6f-9a0b-1c2d3e4f5a86', fb_sport_id, 'Foul Committed', 'FOUL', false, NOW())
    ON CONFLICT (id) DO NOTHING;

    -- -------------------------------------------------------------------------
    -- C. BASKETBALL SEEDING
    -- -------------------------------------------------------------------------
    INSERT INTO sports (id, name, shortname, defaultconfigid) 
    VALUES (bb_sport_id, 'Basketball', 'BB', bb_config_id)
    ON CONFLICT DO NOTHING;

    INSERT INTO playerpositiondefinitions (id, sportid, name, shortname) VALUES 
    ('8b4c0e3c-9d5e-6f7a-0b1c-2d3e4f5a6b61', bb_sport_id, 'Point Guard', 'PG'),
    ('8b4c0e3c-9d5e-6f7a-0b1c-2d3e4f5a6b62', bb_sport_id, 'Shooting Guard', 'SG'),
    ('8b4c0e3c-9d5e-6f7a-0b1c-2d3e4f5a6b63', bb_sport_id, 'Small Forward', 'SF'),
    ('8b4c0e3c-9d5e-6f7a-0b1c-2d3e4f5a6b64', bb_sport_id, 'Power Forward', 'PF'),
    ('8b4c0e3c-9d5e-6f7a-0b1c-2d3e4f5a6b65', bb_sport_id, 'Center', 'C')
    ON CONFLICT (id) DO NOTHING;

    INSERT INTO sportconfigurations (id, sportid, usescleantime, periodscount, perioddurationminutes, fieldsize, rosterlimit, lineuplimit, activeplayerslimit)
    VALUES (bb_config_id, bb_sport_id, true, 4, 10, '28x15m', 12, 12, 5)
    ON CONFLICT (id) DO NOTHING;

    INSERT INTO eventdefinitions (id, sportid, name, shortname, ispositive, createdat) VALUES 
    ('8b4c0e3c-9d5e-6f7a-0b1c-2d3e4f5a6b81', bb_sport_id, 'Field Goal', 'FG', true, NOW()),
    ('8b4c0e3c-9d5e-6f7a-0b1c-2d3e4f5a6b82', bb_sport_id, 'Three-Pointer', '3PT', true, NOW()),
    ('8b4c0e3c-9d5e-6f7a-0b1c-2d3e4f5a6b83', bb_sport_id, 'Assist', 'AST', true, NOW()),
    ('8b4c0e3c-9d5e-6f7a-0b1c-2d3e4f5a6b84', bb_sport_id, 'Rebound', 'REB', true, NOW()),
    ('8b4c0e3c-9d5e-6f7a-0b1c-2d3e4f5a6b85', bb_sport_id, 'Turnover', 'TO', false, NOW()),
    ('8b4c0e3c-9d5e-6f7a-0b1c-2d3e4f5a6b86', bb_sport_id, 'Personal Foul', 'PF', false, NOW())
    ON CONFLICT (id) DO NOTHING;

END $$;

-- ==========================================
-- 3. BASE CLUB (TTA Training Club)
-- ==========================================

INSERT INTO clubs (id, cityid, name, createdat) VALUES 
('11111111-1111-1111-1111-000000000001', 
    (SELECT c.id FROM cities c JOIN regions r ON c.regionid = r.id JOIN countries co ON r.countryid = co.id 
     WHERE c.name = 'Dnipro' AND r.name = 'Dnipropetrovsk Oblast' AND co.code = 'UKR' LIMIT 1), 'TTA Training Club', NOW())
ON CONFLICT (id) DO NOTHING;

-- ==========================================
-- 4. USERS (Without pre-assigned clubs/teams)
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
-- 5. PLAYERS (50 Static Club Players)
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