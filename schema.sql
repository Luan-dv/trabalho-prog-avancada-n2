-- =============================================================================
-- Simulador Gravitacional — modelo do banco (N2)
-- MySQL 8.x, engine InnoDB (necessária para chaves estrangeiras e transações).
--
-- Uso:  mysql -u root -p < schema.sql
-- (com Docker, o docker-compose.yml executa este script automaticamente na 1ª subida)
-- =============================================================================

CREATE DATABASE IF NOT EXISTS simulador
    CHARACTER SET utf8mb4
    COLLATE utf8mb4_unicode_ci;

USE simulador;

-- -----------------------------------------------------------------------------
-- simulacao: uma linha por execução, com os parâmetros e os tempos medidos.
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS simulacao (
    id                     BIGINT UNSIGNED  NOT NULL AUTO_INCREMENT,
    nome                   VARCHAR(100)     NOT NULL,
    descricao              VARCHAR(500)     NULL,
    origem                 VARCHAR(20)      NOT NULL DEFAULT 'interface',  -- 'interface' ou 'benchmark'
    criada_em              DATETIME(3)      NOT NULL DEFAULT CURRENT_TIMESTAMP(3),

    -- Parâmetros
    qtd_corpos             INT UNSIGNED     NOT NULL,              -- corpos no início
    qtd_interacoes         INT UNSIGNED     NOT NULL,              -- interações pedidas
    passo_tempo_s          DOUBLE           NOT NULL,              -- segundos simulados por interação
    lado_universo_m        DOUBLE           NOT NULL,
    threads                SMALLINT UNSIGNED NOT NULL,
    semente                INT              NULL,                  -- permite gerar o mesmo universo de novo

    -- Resultado (preenchido ao final; NULL enquanto a simulação roda)
    status                 ENUM('em_andamento', 'concluida', 'interrompida')
                                            NOT NULL DEFAULT 'em_andamento',
    interacoes_concluidas  INT UNSIGNED     NOT NULL DEFAULT 0,
    qtd_corpos_final       INT UNSIGNED     NULL,                  -- diminui com as fusões
    tempo_total_ms         DOUBLE           NULL,
    tempo_calculo_ms       DOUBLE           NULL,
    tempo_gravacao_ms      DOUBLE           NULL,

    PRIMARY KEY (id),
    -- A tela de replay lista as simulações da mais recente para a mais antiga
    INDEX ix_simulacao_criada_em (criada_em)
) ENGINE = InnoDB;

-- -----------------------------------------------------------------------------
-- corpo: dados que não mudam ao longo da simulação (nome). Separar daqui evita
-- repetir o texto do nome em cada uma das (corpos × interações) linhas de estado.
-- O corpo_id é o Id do corpo no programa: único só DENTRO da simulação, por isso a
-- chave primária é composta.
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS corpo (
    simulacao_id  BIGINT UNSIGNED NOT NULL,
    corpo_id      INT UNSIGNED    NOT NULL,
    nome          VARCHAR(100)    NOT NULL,

    PRIMARY KEY (simulacao_id, corpo_id),
    CONSTRAINT fk_corpo_simulacao
        FOREIGN KEY (simulacao_id) REFERENCES simulacao (id)
        ON DELETE CASCADE        -- excluir a simulação apaga tudo que pertence a ela
) ENGINE = InnoDB;

-- -----------------------------------------------------------------------------
-- estado_corpo: TODAS as informações de cada corpo em CADA interação.
-- interacao = 0 é o estado inicial; interacao = k é o estado após k passos.
--
-- Chave primária (simulacao_id, interacao, corpo_id):
--   * garante que não existam dois estados do mesmo corpo na mesma interação;
--   * no InnoDB a chave primária é o índice CLUSTERIZADO: as linhas ficam fisicamente
--     ordenadas por simulação → interação → corpo. A consulta do replay
--     (WHERE simulacao_id = ? AND interacao BETWEEN ? AND ?) lê então páginas
--     contíguas do disco, sem precisar de um índice separado nem de acesso aleatório.
--     Ou seja, a própria PK é o índice "simulacao_id + interacao" pedido para o replay.
--   * a gravação também acontece nessa ordem (interação crescente), então os INSERTs
--     sempre acrescentam no fim do índice, sem dividir páginas no meio.
--
-- Aceleração e força ficam NULL na interação 0 (ainda não houve passo de cálculo) e
-- quando o valor não é finito (o MySQL não armazena NaN/Infinito em DOUBLE).
-- Força = massa × aceleração; está gravada mesmo sendo derivável porque o enunciado
-- pede aceleração/força explicitamente e assim consultas SQL não precisam recalculá-la.
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS estado_corpo (
    simulacao_id    BIGINT UNSIGNED NOT NULL,
    interacao       INT UNSIGNED    NOT NULL,
    corpo_id        INT UNSIGNED    NOT NULL,

    massa_kg        DOUBLE NOT NULL,
    densidade_kg_m3 DOUBLE NOT NULL,
    raio_m          DOUBLE NOT NULL,
    pos_x_m         DOUBLE NULL,
    pos_y_m         DOUBLE NULL,
    vel_x_m_s       DOUBLE NULL,
    vel_y_m_s       DOUBLE NULL,
    acel_x_m_s2     DOUBLE NULL,
    acel_y_m_s2     DOUBLE NULL,
    forca_x_n       DOUBLE NULL,
    forca_y_n       DOUBLE NULL,

    PRIMARY KEY (simulacao_id, interacao, corpo_id),

    -- Necessário para a chave estrangeira abaixo (o InnoDB exige índice começando pelas
    -- colunas da FK) e útil por si só: consultar a trajetória de UM corpo ao longo do tempo.
    -- Custo assumido: cada INSERT atualiza também este índice secundário.
    INDEX ix_estado_corpo_trajetoria (simulacao_id, corpo_id, interacao),

    CONSTRAINT fk_estado_corpo
        FOREIGN KEY (simulacao_id, corpo_id) REFERENCES corpo (simulacao_id, corpo_id)
        ON DELETE CASCADE
) ENGINE = InnoDB;

-- -----------------------------------------------------------------------------
-- fusao: em que interação cada corpo foi absorvido e por qual. Explica, no replay,
-- por que um corpo deixa de aparecer. Um corpo só pode ser absorvido uma vez.
-- -----------------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS fusao (
    simulacao_id         BIGINT UNSIGNED NOT NULL,
    corpo_absorvido_id   INT UNSIGNED    NOT NULL,
    corpo_absorvedor_id  INT UNSIGNED    NOT NULL,
    interacao            INT UNSIGNED    NOT NULL,

    PRIMARY KEY (simulacao_id, corpo_absorvido_id),
    INDEX ix_fusao_interacao (simulacao_id, interacao),
    INDEX ix_fusao_absorvedor (simulacao_id, corpo_absorvedor_id),

    CONSTRAINT fk_fusao_absorvido
        FOREIGN KEY (simulacao_id, corpo_absorvido_id) REFERENCES corpo (simulacao_id, corpo_id)
        ON DELETE CASCADE,
    CONSTRAINT fk_fusao_absorvedor
        FOREIGN KEY (simulacao_id, corpo_absorvedor_id) REFERENCES corpo (simulacao_id, corpo_id)
        ON DELETE CASCADE
) ENGINE = InnoDB;
